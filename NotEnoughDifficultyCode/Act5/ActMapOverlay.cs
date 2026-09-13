using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace NotEnoughDifficulty.NotEnoughDifficultyCode;

/// <summary>
///     地图**边缘雾气**叠加层（程序化，没有任何贴图素材）。
///
/// ## 用户口径（逐条对应）
/// <list type="bullet">
///   <item><i>"我希望的是一种好看的，带有氛围感的淡紫色雾气在周围"</i>
///         ⇒ 雾气只在**四周边缘**（暗角式遮罩：越靠边越浓、向中心衰减），内部保持原样，不整片铺色。</item>
///   <item><i>"不是这样变色啊，是地图边缘的的纹理变色"</i>
///         ⇒ 染色只作用在**边缘雾**上：遭遇某层敌人 → 边缘雾变该层颜色；
///         踩问号/商店/宝箱/火堆 → 回淡紫。</item>
///   <item><i>"这个不停的阴影重复太丑了，删除"</i>
///         ⇒ 上一版的横向条纹（horizon 乘子 + 高频噪声）**整段删除**；
///         现在只有一层低频、低对比、无硬边的雾，不会出现重复条纹。</item>
/// </list>
///
/// ## 为什么"自己加一层"
/// <c>NMapBg</c> 的三个背景 <c>TextureRect</c> 本身挂着原版 ShaderMaterial，
/// 往上面设自定义 uniform 完全无效（实测三连失效）；而 <c>_mapBgContainer</c> 是 VBoxContainer，
/// **容器会接管子节点布局**、顶掉 FullRect 锚点 ⇒ 挂它下面尺寸变 0、什么都看不见。
/// 所以挂到普通 <c>Control</c>（<c>_mapContainer</c>）下，用自己的材质。
/// </summary>
internal static class ActMapOverlay
{
    private const string OverlayName = "NotEnoughDifficultyMapOverlay";

    private const string ShaderCode = @"
shader_type canvas_item;
render_mode blend_mix;

uniform float pattern_seed = 0.13;
uniform float mist_strength = 0.16;
uniform float mist_start = 0.02;
uniform float mist_end = 0.40;
uniform vec4 mist_color : source_color = vec4(0.72, 0.62, 0.94, 1.0);

uniform float flow_speed = 0.06;
uniform vec4 flow_color : source_color = vec4(0.86, 0.80, 1.00, 1.0);

float hash21(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32 + pattern_seed);
    return fract(p.x * p.y);
}

float vnoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash21(i);
    float b = hash21(i + vec2(1.0, 0.0));
    float c = hash21(i + vec2(0.0, 1.0));
    float d = hash21(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

void fragment() {
    // 1) 暗角式边缘遮罩：0 = 贴边，0.5 = 正中
    float edge = min(min(UV.x, 1.0 - UV.x), min(UV.y, 1.0 - UV.y));
    // 覆盖整张地图：整体铺满，仅在最外圈略浓一点
    float rim = 0.86 + 0.14 * (1.0 - smoothstep(mist_start, mist_end, edge));

    // 2) 一层低频、低对比的柔和扰动：无硬边、无重复条纹
    float soft = vnoise(UV * 2.4 + vec2(pattern_seed * 7.0, pattern_seed * 3.0));
    float mist = rim * (0.75 + 0.25 * soft);

    // 3) 极淡的呼吸感（整体缓慢明灭，不做横扫）
    float breathe = 0.92 + 0.08 * sin(TIME * flow_speed * 6.283 + pattern_seed * 6.283);

    vec3 col = mix(mist_color.rgb, flow_color.rgb, 0.25 * (1.0 - rim));
    COLOR = vec4(col, mist * mist_strength * breathe);
}
";

    private static string _lastUniforms = "<未采集>";

    private static Shader? _shader;
    private static ShaderMaterial? _material;
    private static int _lastFloor = int.MinValue;

    /// <summary>确保叠加层存在（SetMap 之后调用；重复调用不会叠加第二层）。</summary>
    internal static void Ensure(Node? parent, Node? mapBg, bool myth)
    {
        try
        {
            if (parent == null)
            {
                MainFile.Logger.Warn("[Overlay] 取不到地图容器，跳过边缘雾气");
                return;
            }

            if (parent.GetNodeOrNull<ColorRect>(OverlayName) != null)
            {
                if (_material != null) ApplyMode(myth);
                return;
            }

            _shader ??= new Shader { Code = ShaderCode };
            _material = new ShaderMaterial { Shader = _shader };
            ApplyMode(myth);
            _lastFloor = int.MinValue;

            var overlay = new ColorRect
            {
                Name = OverlayName,
                Color = new Color(1, 1, 1, 1),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Material = _material,
                ZIndex = 1,
            };

            parent.AddChild(overlay);
            parent.MoveChild(overlay, 0);      // 放在最底层（背景之上、地图节点之下），只染地图美术
            overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            overlay.Size = (parent as Control)?.Size ?? new Vector2(1920, 1080);
            overlay.Position = Vector2.Zero;

            MainFile.DebugLog($"[Overlay] 边缘雾气层已挂上（神话档={myth}）");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[Overlay] 挂边缘雾气失败: {ex}");
        }
    }

    private static void ApplyMode(bool myth)
    {
        if (_material == null) return;

        // 默认（没有层色时）：淡紫雾；神话档：血雾
        _material.SetShaderParameter("mist_color", myth
            ? new Color(0.80f, 0.40f, 0.42f)
            : new Color(0.72f, 0.62f, 0.94f));
        _material.SetShaderParameter("flow_color", myth
            ? new Color(0.95f, 0.35f, 0.32f)
            : new Color(0.86f, 0.80f, 1.00f));
        _material.SetShaderParameter("mist_strength", myth ? 0.22f : 0.16f);
        _material.SetShaderParameter("flow_speed", myth ? 0.05f : 0.06f);
    }

    /// <summary>
    ///     把**地图自带的元素**（边缘装饰条纹、节点图标、BOSS 图标）也染成该层颜色。
    ///
    /// 那些条纹原本是被 <c>ActModel.MapBgColor / MapTraveledColor / MapUntraveledColor</c>
    /// 通过原版材质（参数名 <c>map_color</c> / <c>black_layer_color</c>）染成紫的 ——
    /// 用户在 act4 里看到的亮紫边线就是它。所以在切色时把这些材质参数一起改掉。
    /// </summary>
    internal static void ApplyToMapElements(NMapScreen screen, Color color, bool dimmed)
    {
        try
        {
            var count = 0;
            foreach (var mat in CollectMaterials(screen))
            {
                mat.SetShaderParameter("map_color", dimmed ? color.Darkened(0.35f) : color);
                mat.SetShaderParameter("black_layer_color", dimmed ? color.Darkened(0.55f) : color.Darkened(0.25f));
                count++;
            }

            MainFile.DebugLog($"[Overlay] 地图元素（边缘条纹/图标）染色 {color}，材质 {count} 个 | uniform: {_lastUniforms}");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[Overlay] 地图元素染色失败: {ex.Message}");
        }
    }

    private static IEnumerable<ShaderMaterial> CollectMaterials(Node root)
    {
        if (root is CanvasItem item && item.Material is ShaderMaterial m) yield return m;

        foreach (var child in root.GetChildren())
            foreach (var deep in CollectMaterials(child))
                yield return deep;
    }
    /// <summary>每次爬楼换一套雾的形态（只改 uniform，不重算贴图）。</summary>
    internal static void SetFloor(int floor)
    {
        if (_material == null || floor == _lastFloor) return;
        _lastFloor = floor;

        var seed = (floor % 89) / 89f + 0.11f;
        _material.SetShaderParameter("pattern_seed", seed);
        _material.SetShaderParameter("mist_end", 0.36f + (floor % 5) * 0.025f);

        MainFile.DebugLog($"[Overlay] 边缘雾换形（楼层={floor} seed={seed:F3}）");
    }

    /// <summary>遭遇某层敌人：**边缘雾**染该层色；传 0 恢复淡紫。</summary>
    internal static void SetLayerTint(int layer)
    {
        if (_material == null) return;

        var color = layer switch
        {
            1 => new Color(0.55f, 0.90f, 0.55f),      // 密林=绿
            2 => new Color(0.45f, 0.90f, 0.92f),      // 巢穴=青
            3 => new Color(0.97f, 0.86f, 0.42f),      // 荣耀=金
            _ => new Color(0.72f, 0.62f, 0.94f),      // 默认淡紫
        };

        _material.SetShaderParameter("mist_color", color);
        _material.SetShaderParameter("mist_strength", layer is >= 1 and <= 3 ? 0.24f : 0.16f);

        MainFile.DebugLog(layer is >= 1 and <= 3
            ? $"[Overlay] 边缘雾染成第 {layer} 层色 {color}"
            : "[Overlay] 边缘雾恢复淡紫");
    }
}
