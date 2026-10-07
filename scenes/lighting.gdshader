shader_type canvas_item;

// The light map over the world. The world is already Apollo, so instead of multiplying (which goes
// muddy and ends in black) each colour steps down its own ramp of darker palette colours, one step
// per band of lost light. Every ramp ends in 172038 or 151d28. Same list as src/hud/Palette.cs.
uniform sampler2D screen_texture : hint_screen_texture, filter_nearest;
uniform float steps = 3.0;

const vec3 PALETTE[46] = vec3[](
    vec3(235, 237, 233), vec3(168, 181, 178), vec3(129, 151, 150), vec3(87, 114, 119),
    vec3(57, 74, 80), vec3(30, 29, 57), vec3(21, 29, 40), vec3(23, 32, 56),
    vec3(37, 58, 94), vec3(60, 94, 139), vec3(79, 143, 186), vec3(164, 221, 219),
    vec3(115, 190, 211), vec3(218, 134, 62), vec3(165, 48, 48), vec3(122, 54, 123),
    vec3(64, 39, 81), vec3(122, 72, 65), vec3(173, 119, 87), vec3(199, 207, 204),
    vec3(168, 202, 88), vec3(117, 167, 67), vec3(37, 86, 46), vec3(25, 51, 45),
    vec3(70, 130, 50), vec3(208, 218, 145), vec3(223, 132, 165), vec3(198, 81, 151),
    vec3(65, 29, 49), vec3(77, 43, 50), vec3(192, 148, 115), vec3(215, 181, 148),
    vec3(231, 213, 179), vec3(52, 28, 39), vec3(96, 44, 44), vec3(136, 75, 43),
    vec3(190, 119, 43), vec3(222, 158, 65), vec3(232, 193, 112), vec3(36, 21, 39),
    vec3(117, 36, 56), vec3(207, 87, 60), vec3(162, 62, 140), vec3(9, 10, 20),
    vec3(16, 20, 31), vec3(32, 46, 55));

// the next darker colour for each one, down Apollo's own ramps: greys to ink, blues and purples
// to night, greens and reds to ink, browns through the dark oranges
const int DARKER[46] = int[](
    19, 2, 3, 4, 45, 7, 6, 6, 7, 8, 9, 12,
    10, 41, 40, 16, 5, 29, 17, 1, 21, 24, 23, 6,
    22, 20, 27, 42, 39, 33, 18, 30, 31, 39, 33, 34,
    35, 36, 37, 6, 28, 14, 15, 43, 44, 6);

int nearest(vec3 c)
{
    int best = 0;
    float distance = 1e9;
    for (int i = 0; i < 46; i++)
    {
        vec3 d = c - PALETTE[i];
        float e = dot(d, d);
        if (e < distance)
        {
            distance = e;
            best = i;
        }
    }
    return best;
}

void fragment()
{
    int index = nearest(texture(screen_texture, SCREEN_UV).rgb * 255.0);
    vec3 light = texture(TEXTURE, UV).rgb;
    float level = clamp(max(light.r, max(light.g, light.b)), 0.0, 1.0);
    int down = int(floor((1.0 - level) * steps + 0.5));
    for (int i = 0; i < 4; i++)
    {
        if (i < down)
        {
            index = DARKER[index];
        }
    }
    COLOR = vec4(PALETTE[index] / 255.0, 1.0);
}
