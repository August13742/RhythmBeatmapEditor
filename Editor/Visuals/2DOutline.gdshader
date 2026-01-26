shader_type canvas_item;

uniform vec4 color : source_color = vec4(1.0);
uniform float width : hint_range(0, 10) = 1.0;
uniform int pattern : hint_range(0, 2) = 0; // 0: Diamond, 1: Circle, 2: Square
uniform bool inside = false;
uniform bool add_margins = true;

void vertex() {
    if (add_margins && !inside) {
        // Expand the mesh to accommodate the outline width
        // UV * 2.0 - 1.0 gives a vector from the center (-1 to 1)
        VERTEX += sign(UV * 2.0 - 1.0) * width;
    }
}

bool hasContraryNeighbour(vec2 uv, vec2 pixel_size, sampler2D tex) {
    float ceil_width = ceil(width);
    
    for (float i = -ceil_width; i <= ceil_width; i++) {
        // Clamp x to width to handle non-integer widths
        float x = (abs(i) > width) ? width * sign(i) : i;
        float offset;
        
        if (pattern == 0) {
            offset = width - abs(x);
        } else if (pattern == 1) {
            offset = floor(sqrt(max(0.0, pow(width + 0.5, 2.0) - x * x)));
        } else {
            offset = width;
        }
        
        for (float j = -ceil(offset); j <= ceil(offset); j++) {
            float y = (abs(j) > offset) ? offset * sign(j) : j;
            vec2 xy = uv + pixel_size * vec2(x, y);
            
            // Check if neighbor is outside texture bounds or transparent
            bool is_empty = (xy != clamp(xy, vec2(0.0), vec2(1.0)) || texture(tex, xy).a <= 0.0);
            if (is_empty == inside) {
                return true;
            }
        }
    }
    return false;
}

void fragment() {
    vec2 uv = UV;
    vec2 pixel_size = TEXTURE_PIXEL_SIZE;
    
    if (add_margins && !inside) {
        // Adjust UVs to account for the expanded vertex size
        vec2 texture_size = 1.0 / TEXTURE_PIXEL_SIZE;
        vec2 new_size = texture_size + vec2(width * 2.0);
        pixel_size = 1.0 / new_size;
        
        uv = (UV - 0.5) * (new_size / texture_size) + 0.5;
        
        if (uv != clamp(uv, vec2(0.0), vec2(1.0))) {
            COLOR.a = 0.0;
        } else {
            COLOR = texture(TEXTURE, uv);
        }
    } else {
        COLOR = texture(TEXTURE, uv);
    }
    
    bool is_current_filled = COLOR.a > 0.0;
    if (is_current_filled == inside && hasContraryNeighbour(uv, TEXTURE_PIXEL_SIZE, TEXTURE)) {
        if (inside) {
            COLOR.rgb = mix(COLOR.rgb, color.rgb, color.a);
        } else {
            COLOR.rgb = color.rgb;
        }
        COLOR.a += (1.0 - COLOR.a) * color.a;
    }
}