shader_type canvas_item;

// Snaps everything drawn under it to the nearest Apollo colour: lighting, fog, the fight overlay
// and text edges on the map all end up in the palette. Same list as src/hud/Palette.cs.
uniform sampler2D screen_texture : hint_screen_texture, filter_nearest;

const int COUNT = 46;
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

void fragment()
{
    vec3 c = texture(screen_texture, SCREEN_UV).rgb * 255.0;
    vec3 best = PALETTE[0];
    float distance = 1e9;
    for (int i = 0; i < COUNT; i++)
    {
        vec3 d = c - PALETTE[i];
        float e = dot(d, d);
        if (e < distance)
        {
            distance = e;
            best = PALETTE[i];
        }
    }
    COLOR = vec4(best / 255.0, 1.0);
}
