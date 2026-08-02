sampler2D inputSampler : register(s0);

float inputWidth : register(c0);
float inputHeight : register(c1);
float blurLength : register(c2);
float maximumRadius : register(c3);
float directionX : register(c4);
float directionY : register(c5);

float2 ClampSampleCoordinate(float2 uv, float2 halfTexel)
{
    return clamp(uv, halfTexel, 1.0 - halfTexel);
}

float4 SampleInput(float2 uv, float2 halfTexel)
{
    float2 sampleUv = ClampSampleCoordinate(uv, halfTexel);
    return tex2Dlod(inputSampler, float4(sampleUv, 0.0, 0.0));
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 inputSize = max(float2(inputWidth, inputHeight), float2(1.0, 1.0));
    float2 halfTexel = 0.5 / inputSize;

    float effectiveBlurLength = max(blurLength, 0.0001);
    float progress = saturate((uv.y * inputSize.y) / effectiveBlurLength);
    [branch]
    if (progress >= 1.0 || maximumRadius <= 0.0)
        return SampleInput(uv, halfTexel);

    float smoothedProgress = progress * progress * (3.0 - (2.0 * progress));
    float localRadius = maximumRadius * (1.0 - smoothedProgress);

    // Symmetric normalized 14-tap Gaussian kernel centered between samples.
    const float weight1 = 0.1980261332;
    const float weight2 = 0.1522332874;
    const float weight3 = 0.0899671292;
    const float weight4 = 0.0408738191;
    const float weight5 = 0.0142755777;
    const float weight6 = 0.0038329159;
    const float weight7 = 0.0007911375;

    float2 direction = float2(directionX, directionY);
    float2 sampleStep = (direction / inputSize) * (localRadius / 6.5);

    float4 color = 0.0;
    color += SampleInput(uv + (sampleStep * 0.5), halfTexel) * weight1;
    color += SampleInput(uv - (sampleStep * 0.5), halfTexel) * weight1;
    color += SampleInput(uv + (sampleStep * 1.5), halfTexel) * weight2;
    color += SampleInput(uv - (sampleStep * 1.5), halfTexel) * weight2;
    color += SampleInput(uv + (sampleStep * 2.5), halfTexel) * weight3;
    color += SampleInput(uv - (sampleStep * 2.5), halfTexel) * weight3;
    color += SampleInput(uv + (sampleStep * 3.5), halfTexel) * weight4;
    color += SampleInput(uv - (sampleStep * 3.5), halfTexel) * weight4;
    color += SampleInput(uv + (sampleStep * 4.5), halfTexel) * weight5;
    color += SampleInput(uv - (sampleStep * 4.5), halfTexel) * weight5;
    color += SampleInput(uv + (sampleStep * 5.5), halfTexel) * weight6;
    color += SampleInput(uv - (sampleStep * 5.5), halfTexel) * weight6;
    color += SampleInput(uv + (sampleStep * 6.5), halfTexel) * weight7;
    color += SampleInput(uv - (sampleStep * 6.5), halfTexel) * weight7;
    return color;
}
