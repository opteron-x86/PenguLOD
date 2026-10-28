using AnvilLOD.Textures;
using AnvilLOD.Textures.Bc;
using Xunit;

namespace AnvilLOD.Core.Tests;

public class TextureComputeTests
{
    [Fact]
    public void CpuModePreservesOutputAndRejectsInvalidDimensions()
    {
        var pixels=Enumerable.Repeat((byte)255,64).ToArray();var expected=Bc7Encoder.EncodeImage(pixels,4,4);
        using(TextureCompute.Enter("Cpu"))Assert.Equal(expected,Bc7Encoder.EncodeImage(pixels,4,4));
        Assert.Throws<ArgumentException>(()=>Bc7Encoder.EncodeImage(pixels,8,8));
        Assert.Throws<ArgumentException>(()=>TextureCompute.Enter("unknown"));
    }
    [Fact]
    public void AlphaImagesRetainCpuModeSelectionUnderVulkanScope()
    {
        var pixels=new byte[256*256*4];for(int i=0;i<pixels.Length;i+=4){pixels[i]=190;pixels[i+1]=80;pixels[i+3]=(byte)(i%32==0?0:255);}
        var expected=Bc7Encoder.EncodeImage(pixels,256,256);
        using var compute=TextureCompute.Enter("Vulkan");
        Assert.Equal(expected,Bc7Encoder.EncodeImage(pixels,256,256));Assert.Null(compute.Measurement);
    }
    [VulkanFact]
    public void RealVulkanDispatchPassesDecodedQualityCheckIncludingSoftwareDriver()
    {
        var result=TextureCompute.Benchmark(allowSoftware:true);
        Assert.True(result.Available,result.Recommendation);Assert.True(result.QualityPassed,result.ToString());
        Assert.True(result.CpuMilliseconds>0);Assert.True(result.VulkanMilliseconds>0);
    }
    private sealed class VulkanFactAttribute : FactAttribute
    {public VulkanFactAttribute(){if(Environment.GetEnvironmentVariable("PENGULOD_TEST_VULKAN")!="1")Skip="Set PENGULOD_TEST_VULKAN=1 after building the optional native compute backend.";}}
}
