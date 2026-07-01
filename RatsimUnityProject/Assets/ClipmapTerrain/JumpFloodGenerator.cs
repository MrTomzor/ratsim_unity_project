using UnityEngine;

namespace ClipmapTerrain {
    public static class JumpFloodGenerator {
        
        /// <summary>
        /// Generates a distance field texture using the Jump Flooding Algorithm.
        /// </summary>
        /// <param name="jfaShader">The JumpFloodSDF Compute Shader</param>
        /// <param name="inputSeedTexture">Input texture where R >= 0.99 represents a seed</param>
        /// <param name="sideLength">The world-space length of the texture (used to scale the distance)</param>
        /// <returns>An RFloat RenderTexture containing the absolute world distance to the nearest seed.</returns>
        public static RenderTexture GenerateDistanceTexture(ComputeShader jfaShader, Texture inputSeedTexture, float sideLength, int channelIndex = 0) {
            int resolution = inputSeedTexture.width;
            
            if (resolution != inputSeedTexture.height) {
                Debug.LogError("JumpFloodGenerator: Input texture must be square!");
                return null;
            }
            if (!Mathf.IsPowerOfTwo(resolution)) {
                Debug.LogWarning("JumpFloodGenerator: Input texture is not a power of two. JFA works best with power-of-two textures.");
            }

            // Create ping pong buffers for UV coordinates (RGFloat)
            RenderTextureDescriptor uvDesc = new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.RGFloat, 0);
            uvDesc.enableRandomWrite = true;
            RenderTexture bufferA = RenderTexture.GetTemporary(uvDesc);
            RenderTexture bufferB = RenderTexture.GetTemporary(uvDesc);
            
            // Create final output texture for distance (RFloat to hold absolute world units)
            RenderTextureDescriptor distDesc = new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.RFloat, 0);
            distDesc.enableRandomWrite = true;
            RenderTexture outputTexture = new RenderTexture(distDesc);
            outputTexture.name = "JumpFlood_DistanceMap";
            outputTexture.Create();

            // Setup kernels
            int initKernel = jfaShader.FindKernel("InitPass");
            int jumpKernel = jfaShader.FindKernel("JumpPass");
            int distKernel = jfaShader.FindKernel("DistancePass");

            // Dispatch 8x8 thread groups
            int threadGroups = Mathf.CeilToInt(resolution / 8f);

            // --- 1. Init Pass ---
            jfaShader.SetFloat("Resolution", resolution);
            jfaShader.SetInt("ChannelIndex", channelIndex);
            jfaShader.SetTexture(initKernel, "InputTexture", inputSeedTexture);
            jfaShader.SetTexture(initKernel, "WriteBuffer", bufferA);
            jfaShader.Dispatch(initKernel, threadGroups, threadGroups, 1);

            // --- 2. Jump Passes ---
            int stepSize = resolution / 2;
            RenderTexture readBuffer = bufferA;
            RenderTexture writeBuffer = bufferB;

            while (stepSize > 0) {
                jfaShader.SetFloat("StepSize", stepSize);
                jfaShader.SetTexture(jumpKernel, "ReadBuffer", readBuffer);
                jfaShader.SetTexture(jumpKernel, "WriteBuffer", writeBuffer);
                
                jfaShader.Dispatch(jumpKernel, threadGroups, threadGroups, 1);

                // Swap buffers for the next pass
                RenderTexture temp = readBuffer;
                readBuffer = writeBuffer;
                writeBuffer = temp;

                stepSize /= 2;
            }

            // --- 3. Final Distance Pass ---
            jfaShader.SetFloat("SideLength", sideLength);
            // After the loop, the final result is in readBuffer
            jfaShader.SetTexture(distKernel, "ReadBuffer", readBuffer); 
            jfaShader.SetTexture(distKernel, "OutputDistance", outputTexture);
            jfaShader.Dispatch(distKernel, threadGroups, threadGroups, 1);

            // Cleanup temp buffers
            RenderTexture.ReleaseTemporary(bufferA);
            RenderTexture.ReleaseTemporary(bufferB);

            return outputTexture; // Note: Caller is responsible for releasing this RenderTexture when done
        }
    }
}
