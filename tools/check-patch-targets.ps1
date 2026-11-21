<#
  校验所有 Harmony patch 目标（类型 + 成员）在当前程序集元数据中是否存在。

  数据源是 DumpApi 导出的文本（tools/DumpApi）。游戏或 BaseLib 升级后先重新导出：

    DumpApi.exe "<游戏>\data_sts2_windows_x86_64\sts2.dll" <out>\sts2.api.txt "<游戏>\data_sts2_windows_x86_64;<仓库上级>\_refs\game"
    DumpApi.exe "<workshop>\BaseLib\BaseLib.dll"             <out>\BaseLib.api.txt "<workshop>\BaseLib;<仓库上级>\_refs\game"

  用法：
    pwsh -File tools\check-patch-targets.ps1
    pwsh -File tools\check-patch-targets.ps1 -ApiSts2 D:\somewhere\sts2.api.txt -ApiBaseLib D:\somewhere\BaseLib.api.txt
#>
param(
    [string]$SourceDir,
    [string]$ApiSts2,
    [string]$ApiBaseLib,
    [string]$ApiDir,
    [string]$CsvOut
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $SourceDir) { $SourceDir = Join-Path $repoRoot 'NotEnoughDifficultyCode' }
if (-not $ApiDir) { $ApiDir = Join-Path (Split-Path -Parent (Split-Path -Parent $repoRoot)) '_tmp-inspect' }
if (-not $ApiSts2) { $ApiSts2 = Join-Path $ApiDir 'sts2.api.txt' }
if (-not $ApiBaseLib) { $ApiBaseLib = Join-Path $ApiDir 'BaseLib.api.txt' }
if (-not $CsvOut) { $CsvOut = Join-Path $repoRoot ('docs\patch-target-check.csv') }

foreach ($f in @($ApiSts2, $ApiBaseLib)) {
    if (-not (Test-Path $f)) { throw "API dump not found: $f (run tools/DumpApi first, or pass -ApiSts2 / -ApiBaseLib)" }
}
if (-not (Test-Path $SourceDir)) { throw "source dir not found: $SourceDir" }

# ---------- 1. 建立 API 索引 ----------
$apiTypes = @{}   # fullName -> HashSet[memberName]
$apiIndex = @{}   # simpleName -> fullName[]
$apiFiles = @{ 'sts2' = $ApiSts2; 'BaseLib' = $ApiBaseLib }

foreach ($kv in $apiFiles.GetEnumerator()) {
    $current = $null
    foreach ($line in [System.IO.File]::ReadLines($kv.Value)) {
        if ($line.StartsWith('#')) { continue }
        if ($line.StartsWith("TYPE`t")) {
            $p = $line.Split("`t")
            if ($p.Length -lt 3) { continue }
            $full = $p[2]
            $current = $full
            if (-not $apiTypes.ContainsKey($full)) {
                $apiTypes[$full] = [System.Collections.Generic.HashSet[string]]::new()
            }
            $simple = ($full -split '\.')[-1]
            if (-not $apiIndex.ContainsKey($simple)) { $apiIndex[$simple] = @() }
            if ($apiIndex[$simple] -notcontains $full) { $apiIndex[$simple] += $full }
        }
        elseif ($null -ne $current -and $line.StartsWith("`t")) {
            $p = $line.TrimStart("`t").Split("`t")
            if ($p.Length -ge 2) { [void]$apiTypes[$current].Add($p[1]) }
        }
    }
}
Write-Host "API types indexed: $($apiTypes.Count)  (sts2 + BaseLib)"

# 成员名 -> 拥有该成员的完整类型名集合（用于区分「移除」与「迁移」）
$memberOwners = @{}
foreach ($t in $apiTypes.Keys) {
    foreach ($m in $apiTypes[$t]) {
        $name = ($m -split '\(')[0].Trim()
        if (-not $memberOwners.ContainsKey($name)) { $memberOwners[$name] = @() }
        if ($memberOwners[$name] -notcontains $t) { $memberOwners[$name] += $t }
    }
}

# ---------- 2. 从源码抽取 patch 目标 + using 映射 ----------
# 解析 typeof(...) 里写的类型名：全名优先（源码里常写全名），否则按 using 拼命名空间
$resolveFull = {
    param($rawType, $usings)
    if ($apiTypes.ContainsKey($rawType)) { return $rawType }
    if ($rawType.Contains('.')) { return $rawType }   # 已是全名但元数据里没有 → 保持原样报缺失
    if ($usings.ContainsKey($rawType)) { return "$($usings[$rawType]).$rawType" }
    return $rawType
}

$rows = @()
foreach ($f in Get-ChildItem $SourceDir -Recurse -Filter *.cs) {
    $text = [System.IO.File]::ReadAllText($f.FullName)
    $usings = @{}
    foreach ($m in [regex]::Matches($text, '(?m)^\s*using\s+([\w\.]+)\s*;')) {
        $ns = $m.Groups[1].Value
        $usings[($ns -split '\.')[-1]] = $ns
    }
    foreach ($m in [regex]::Matches($text, '\[HarmonyPatch\(\s*typeof\(([\w\.]+)\)\s*,\s*(?:nameof\(([\w\.]+)\)|"([^"]+)")([^\]]*)\)\s*\]')) {
        $rawType = $m.Groups[1].Value
        $member = if ($m.Groups[2].Success) { $m.Groups[2].Value } else { $m.Groups[3].Value }
        if ($member) { $member = ($member -split '\.')[-1] }
        $rows += [pscustomobject]@{ File = $f.Name; RawType = $rawType; FullType = (& $resolveFull $rawType $usings); Member = $member; ExplicitArgs = ((($m.Groups[4].Value) -match 'typeof\(')) }
    }
    foreach ($m in [regex]::Matches($text, '\[HarmonyPatch\(\s*typeof\(([\w\.]+)\)\s*(?:,\s*MethodType\.[\w]+[^\)]*)?\)\s*\]')) {
        $rawType = $m.Groups[1].Value
        $rows += [pscustomobject]@{ File = $f.Name; RawType = $rawType; FullType = (& $resolveFull $rawType $usings); Member = ''; ExplicitArgs = $true }
    }
}

# ---------- 3. 逐条校验 ----------
$report = foreach ($r in $rows) {
    $typeOk = $apiTypes.ContainsKey($r.FullType)
    $resolvedType = $r.FullType
    if (-not $typeOk -and $apiIndex.ContainsKey($r.RawType)) {
        $cands = $apiIndex[$r.RawType]
        if ($cands.Count -eq 1) { $resolvedType = $cands[0]; $typeOk = $true }
        elseif ($cands.Count -gt 1) { $resolvedType = ($cands -join ' | ') }
    }
    $memberOk = $null
    $movedTo = @()
    $overloads = 0
    $ambiguous = $false
    if ($typeOk -and $r.Member) {
        $sameName = @($apiTypes[$resolvedType] | Where-Object { ($_ -split '\(')[0].Trim() -eq $r.Member })
        $overloads = $sameName.Count
        $memberOk = $sameName | Select-Object -First 1
        # 只写方法名（没列参数类型）而目标有多个重载 ⇒ Harmony 报 Ambiguous match，整类被跳过
        if ($overloads -gt 1 -and -not $r.ExplicitArgs) { $ambiguous = $true }
        if (-not $memberOk -and $memberOwners.ContainsKey($r.Member)) { $movedTo = $memberOwners[$r.Member] }
    }
    [pscustomobject]@{
        File       = $r.File
        Type       = $r.RawType
        FullType   = $resolvedType
        Member     = $r.Member
        TypeFound  = $typeOk
        MemberOk   = if ($r.Member) { [bool]$memberOk } else { $null }
        Overloads  = $overloads
        Ambiguous  = $ambiguous
        Elsewhere  = ($movedTo | Select-Object -First 3) -join ' | '
    }
}

Write-Host "`n===== patch 目标校验结果 ====="
$bad = @($report | Where-Object { -not $_.TypeFound -or $_.MemberOk -eq $false -or $_.Ambiguous })
$typeMissing = @($report | Where-Object { -not $_.TypeFound }).Count
$memberMissing = @($report | Where-Object { $_.MemberOk -eq $false }).Count
$ambiguousCount = @($report | Where-Object { $_.Ambiguous }).Count
Write-Host "总条目: $($report.Count)   类型缺失: $typeMissing   成员缺失: $memberMissing   重载二义: $ambiguousCount"
if ($bad.Count -gt 0) {
    Write-Host "`n--- 有问题的条目 ---"
    $bad | Format-Table File, Type, Member, TypeFound, MemberOk, Overloads, Ambiguous, Elsewhere -AutoSize | Out-String -Width 400 | Write-Host
} else {
    Write-Host "全部 patch 目标（类型 + 成员 + 重载唯一性）在当前程序集中均通过。"
}
$report | Export-Csv $CsvOut -NoTypeInformation -Encoding UTF8
Write-Host "CSV -> $CsvOut"
