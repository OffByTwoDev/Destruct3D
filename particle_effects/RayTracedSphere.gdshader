// Copied from:
// https://github.com/danilw/Godot4-Sphere-as-particle-do-not-use-Sphere-mesh-as-particle

// Full example at: https://danilw.itch.io/particle-effects-godot3

// WARNING - ALPHA does not work in compatibility in Godot 4.3 - use Godot 3.5 or web
// https://github.com/godotengine/godot/issues/85370

shader_type spatial;
render_mode
	blend_mix,
	depth_draw_opaque,
	cull_back,
	diffuse_burley,
	specular_schlick_ggx,
	shadows_disabled,
	depth_prepass_alpha;

/** Whether or not to use normal-based occlusion when shading. */
uniform bool use_occlusion = false;

/** Try to correct perspective distortion. */
uniform bool fix_perspective = false;

/** Whether or not to use colors from lights in the scene. */
uniform bool use_light_color = true;

/** Base color of the sphere. */
uniform vec4 object_color: source_color = vec4(0.5, 0.5, 0.5, 1.0);

/** Light color to use if `use_light_color` is false. */
uniform vec4 base_light_color: source_color = vec4(1.0, 1.0, 1.0, 1.0);

/** Color used for shading the sphere. */
uniform vec4 shade_color: source_color = vec4(0.05, 0.05, 0.05, 1.);

/** Apply cel shading instead of smooth shading. */
uniform bool cel_shading = false;

/** Threshold for cel shading. */
uniform float cel_shade_threshold: hint_range(-1.0, 1.0, 0.001) = 0.1;

/** Softness of cel shading. */
uniform float cel_shade_softness: hint_range(0.0, 1.0, 0.001) = 0.02;

/** How large the sphere is. */
uniform float sphere_size: hint_range(0.01, 1.0, 0.001) = 0.2;

/** Sphere position in world space */
varying vec3 sphere_position;

/** Origin of ray from camera, used for raymarching sphere drawing */
varying vec3 camera_ray_origin;

/** Sphere size */
varying float sp_size;

void vertex() {
	mat4 mat_world = mat4(
		normalize(INV_VIEW_MATRIX[0]) * length(MODEL_MATRIX[0]),
		normalize(INV_VIEW_MATRIX[1]) * length(MODEL_MATRIX[0]),
		normalize(INV_VIEW_MATRIX[2]) * length(MODEL_MATRIX[2]),
		MODEL_MATRIX[3]
	);

	MODELVIEW_MATRIX = VIEW_MATRIX * mat_world;
	sphere_position = mat_world[3].xyz;

	sp_size = 2. / max(length(MODEL_MATRIX[0].xyz), 0.0001);

	camera_ray_origin = INV_VIEW_MATRIX[3].xyz * sp_size;

	if (fix_perspective) {
		VERTEX *= clamp(
			length(sphere_position - INV_VIEW_MATRIX[3].xyz) / (2. / sp_size),
			0.,
			1.
		);
	}
}

/**
 * Calculate cel shading.
 *
 * @param nor Normal.
 * @param light Light direction.
 * @return Shade value.
 */
float cel_shade(vec3 nor, vec3 light)
{
	return smoothstep(
		cel_shade_threshold - cel_shade_softness,
		cel_shade_threshold + cel_shade_softness,
		dot(nor, light)
	);
}

/**
 * Sphere intersection.
 *
 * The MIT License
 * Copyright © 2014 Inigo Quilez
 * https://iquilezles.org/www/articles/intersectors/intersectors.htm
 *
 * @param ro Ray origin.
 * @param rd Ray direction.
 * @param sph Sphere position and radius.
 * @return Distance to intersection.
 */
float sphere_intersect(in vec3 ro, in vec3 rd, in vec4 sph)
{
	vec3 oc = ro - sph.xyz;
	float b = dot(oc, rd);
	float c = dot(oc, oc) - sph.w * sph.w;
	float h = b * b - c;

	if (h < 0.0) {
		return -1.0;
	}

	return -b - sqrt(h);
}

/**
 * Get color for sphere at position.
 *
 * @param rd Ray direction.
 * @param lght Light direction.
 * @param ro Ray origin.
 * @param sp Sphere position.
 * @param sp_sz Sphere size.
 * @param bcol Base color.
 * @return Color.
 */
vec4 sphere_image(vec3 rd, vec3 lght, vec3 ro, vec3 sp, float sp_sz, vec3 bcol)
{
    vec4 sph = vec4(sp, sp_sz);
    vec3 lig = lght;
    vec3 col = vec3(0.0);

    float tmin = 1e10;
    vec3 nor = vec3(0.);
    float occ = 1.0;

	float a = 0.;

    float t2 = sphere_intersect(ro, rd, sph);

	// to fix black color
	t2 = max(t2, 0.001);

    if (t2 > 0.002 && t2 < tmin) {
        tmin = t2;
        vec3 pos = ro + t2 * rd;
		nor = normalize(pos - sph.xyz);
        occ = 0.5 + 0.5 * nor.y;
	}

    if (tmin < 1000.0) {
        vec3 pos = ro + tmin * rd;

		col = vec3(1.0);
		a = 1.;

		float shade_value = 0.;
        if (!cel_shading) {
			shade_value = clamp(
				dot(nor, lig),
				0.0,
				1.0
			);
		} else {
			shade_value = cel_shade(nor, lig);
		}

		if (use_occlusion) {
			shade_value += 0.05 * occ;
		}

		col = mix(
			shade_color.rgb,
			bcol,
			shade_value
		);

		// col *= exp(-0.05 * tmin);
    }

	//col = clamp(col, 0., 1.);
    col = sqrt(col);

    return vec4(col, a);
}

void light(){
	vec3 rd = normalize((INV_VIEW_MATRIX * vec4(normalize(-VIEW), 0.0)).xyz);
	vec3 lgt = normalize((INV_VIEW_MATRIX * vec4(normalize(LIGHT), 0.0)).xyz);

	// proportion fix on zoom
	vec4 col = vec4(0.);

	vec3 lc = base_light_color.rgb;
	if (use_light_color) {
		lc = LIGHT_COLOR;
	}

	if (fix_perspective) {
		col = sphere_image(
			normalize(rd),
			normalize(lgt),
			camera_ray_origin,
			sphere_position * sp_size,
			sphere_size - (
				sphere_size * 0.999 * (
					1. - min(
						length(sphere_position - INV_VIEW_MATRIX[3].xyz) / (2. / sp_size),
						1.
					)
				)
			),
			lc
		);
	} else {
		col = sphere_image(
			normalize(rd),
			normalize(lgt),
			camera_ray_origin,
			sphere_position * sp_size,
			sphere_size,
			lc
		);
	}

	SPECULAR_LIGHT += col.rgb * ATTENUATION * ALBEDO;
	DIFFUSE_LIGHT += col.rgb * ATTENUATION * ALBEDO;
	ALPHA = col.w;
}

void fragment() {
	ALBEDO = object_color.rgb;
	METALLIC = 0.001;
	SPECULAR = 0.5;
	ROUGHNESS = 0.9;
	ALPHA = 0.5;
	ALPHA_SCISSOR_THRESHOLD = 0.5;

	// This is needed ONLY when you have/want particle-shadows
	// and to fix Compatibility render in Godot 4.
	// If you do not need shadows and compatibility - remove this
	{
		vec4 col = vec4(0.);
		vec3 lc = base_light_color.rgb;
		vec3 rd = normalize((INV_VIEW_MATRIX * vec4(normalize(-VIEW), 0.0)).xyz);
		vec3 lgt = vec3(0., 1., 0.);
		if (fix_perspective) {
			col = sphere_image(
				normalize(rd),
				normalize(lgt),
				camera_ray_origin,
				sphere_position * sp_size,
				sphere_size - (
					sphere_size * 0.999 * (
						1. -
						min(
							length(sphere_position - INV_VIEW_MATRIX[3].xyz) / (2. / sp_size),
							1.
						)
					)
				),
				lc
			);
		} else {
			col = sphere_image(
				normalize(rd),
				normalize(lgt),
				camera_ray_origin,
				sphere_position * sp_size,
				sphere_size,
				lc
			);
		}

		ALPHA = col.w;
	}
}