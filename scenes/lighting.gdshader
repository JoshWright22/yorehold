shader_type canvas_item;

// The light map over the world. The world is already CC-29, so instead of multiplying (which goes
// muddy and ends in black) each colour steps down its own ramp of darker palette colours, one step
// per band of lost light. Every ramp ends in 352b42 or 212123. Same list as src/hud/Palette.cs.
uniform sampler2D screen_texture : hint_screen_texture, filter_nearest;
uniform float steps = 3.0;

const vec3 PALETTE[29] = vec3[](
    vec3(242, 240, 229), vec3(184, 181, 185), vec3(134, 129, 136), vec3(100, 99, 101),
    vec3(69, 68, 79), vec3(58, 56, 88), vec3(33, 33, 35), vec3(53, 43, 66),
    vec3(67, 67, 106), vec3(75, 128, 202), vec3(104, 194, 211), vec3(162, 220, 199),
    vec3(237, 225, 158), vec3(211, 160, 104), vec3(180, 82, 82), vec3(106, 83, 110),
    vec3(75, 65, 88), vec3(128, 73, 58), vec3(167, 123, 91), vec3(229, 206, 180),
    vec3(194, 211, 104), vec3(138, 176, 96), vec3(86, 123, 121), vec3(78, 88, 74),
    vec3(123, 114, 67), vec3(178, 180, 126), vec3(237, 200, 196), vec3(207, 138, 203),
    vec3(95, 85, 106));

// the next darker colour for each one: greys to ink, blues and purples to night, greens through
// teal and moss, browns through rust and shade
const int DARKER[29] = int[](
    1, 2, 3, 4, 7, 7, 6, 7,
    5, 8, 9, 22,
    13, 18, 17, 16,
    7, 16, 17, 18,
    21, 22, 23, 7,
    23, 24, 27, 15,
    16);

int nearest(vec3 c)
{
    int best = 0;
    float distance = 1e9;
    for (int i = 0; i < 29; i++)
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
