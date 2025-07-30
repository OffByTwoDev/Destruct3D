shader_type spatial;

const float ThresholdAngleRadians = 0.01f;
const float cosThresholdAngleRadians = cos(ThresholdAngleRadians);

uniform sampler2D exteriorSurfaceMaterial;
uniform sampler2D interiorSurfaceMaterial;

uniform vec3 exteriorSurfaceNormals[100];

uniform vec3 albedo_multiplier;

// vertex() normal is in model space, fragment() normal is in view space
// so we use this variable to pass the model space normal to the fragment function
varying vec3 model_normal;

void vertex()
{
    model_normal = NORMAL;
}

bool IsApproxSameDirection(vec3 firstVector, vec3 secondVector)
{
	vec3 normalisedFirstVector = normalize(firstVector);
	vec3 normalisedSecondVector = normalize(secondVector);

	float cosAngleBetweenVectorsRadians = dot(normalisedFirstVector, normalisedSecondVector);

	if (cosAngleBetweenVectorsRadians > cosThresholdAngleRadians)
	{
		return true;
	}

	return false;
}

// find relevant material & uv origin for this pixel
// if it exists, use it
// if it doesnt exist, then use the fragmentMaterial
void fragment()
{
	bool matched = false;

	for (int i = 0; i < exteriorSurfaceNormals.length(); i++)
	{
		if (IsApproxSameDirection(model_normal, exteriorSurfaceNormals[i]) || IsApproxSameDirection(-model_normal, exteriorSurfaceNormals[i]))
		{
			// draw from exterior material
			matched = true;
			break;
		}
		// draw from interior material
	}

	vec4 exterior_color = texture(exteriorSurfaceMaterial, UV);
	vec4 interior_color = texture(interiorSurfaceMaterial, UV);

	ALBEDO = matched ? exterior_color.xyz : interior_color.xyz;

	// add back in any colour the original material's albedo setting had
	ALBEDO *= albedo_multiplier;
}