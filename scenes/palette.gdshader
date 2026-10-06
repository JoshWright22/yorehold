shader_type canvas_item;

// Snaps everything drawn under it to the nearest CC-29 colour: lighting, fog, the fight overlay
// and text edges on the map all end up in the palette. Same list as src/hud/Palette.cs.
uniform sampler2D screen_texture : hint_screen_texture, filter_nearest;

const int COUNT = 29;
const vec3 PALETTE[29] = vec3[](
    vec3(242, 240, 229), vec3(184, 181, 185), vec3(134, 129, 136), vec3(100, 99, 101),
    vec3(69, 68, 79), vec3(58, 56, 88), vec3(33, 33, 35), vec3(53, 43, 66),
    vec3(67, 67, 106), vec3(75, 128, 202), vec3(104, 194, 211), vec3(162, 220, 199),
    vec3(237, 225, 158), vec3(211, 160, 104), vec3(180, 82, 82), vec3(106, 83, 110),
    vec3(75, 65, 88), vec3(128, 73, 58), vec3(167, 123, 91), vec3(229, 206, 180),
    vec3(194, 211, 104), vec3(138, 176, 96), vec3(86, 123, 121), vec3(78, 88, 74),
    vec3(123, 114, 67), vec3(178, 180, 126), vec3(237, 200, 196), vec3(207, 138, 203),
    vec3(95, 85, 106));

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
