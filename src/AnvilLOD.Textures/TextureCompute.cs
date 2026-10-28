using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Textures.Bc;

namespace AnvilLOD.Textures;

public sealed record ComputeBenchmark(bool Available,string Device,bool Software,bool QualityPassed,
    double CpuMilliseconds,double VulkanMilliseconds,double MeanSquaredDifference,string Recommendation);

/// <summary>Optional opaque-texture acceleration. CPU stays available and handles alpha images and small mips.</summary>
public sealed class TextureCompute : IDisposable
{
    private static readonly AsyncLocal<TextureCompute?> Active=new();
    private readonly TextureCompute? _previous;
    private readonly object _gate=new();
    private readonly IProgress<string>? _progress;
    private readonly string _mode;
    private IntPtr _context;private bool _attempted,_enabled,_disposed;
    private long _gpuImages,_cpuImages;
    public ComputeBenchmark? Measurement {get;private set;}
    private TextureCompute(string mode,IProgress<string>? progress)
    { ValidateMode(mode);_mode=mode;_progress=progress;_previous=Active.Value;Active.Value=this; }
    public static TextureCompute Enter(string mode="Cpu",IProgress<string>? progress=null)=>new(mode,progress);
    public static void ValidateMode(string mode)
    {if(mode is not ("Cpu" or "Auto" or "Vulkan"))throw new ArgumentException("Texture compute must be Cpu, Auto or Vulkan.");}

    internal static bool TryEncode(byte[] rgba,int width,int height,out byte[] output)
    {
        output=[];var active=Active.Value;
        if(active is null || active._mode=="Cpu")return false;
        if(width*height<256*256 || width>4096 || height>4096 || Enumerable.Range(0,width*height).Any(i=>rgba[i*4+3]!=255))
        {Interlocked.Increment(ref active._cpuImages);return false;}
        lock(active._gate)
        {
            if(active._disposed)return false;
            if(!active._attempted)
            {
                active._attempted=true;
                active.Measurement=Measure(allowSoftware:false,out active._context);
                active._enabled=active.Measurement.QualityPassed && (active._mode=="Vulkan" || active.Measurement.VulkanMilliseconds<active.Measurement.CpuMilliseconds*.8);
                active._progress?.Report("Texture compute: "+active.Measurement.Device+"; "+active.Measurement.Recommendation+
                    (active._enabled?" Using Vulkan for large opaque textures.":" Using CPU."));
            }
            if(!active._enabled){active._cpuImages++;return false;}
            RunLimits.Check();
            var message=new StringBuilder(1024);output=new byte[checked(((width+3)/4)*((height+3)/4)*16)];
            if(pgl_encode(active._context,rgba,(uint)width,(uint)height,output,message,message.Capacity)==0)
            {
                active._enabled=false;active._progress?.Report("Vulkan encode failed; continuing on CPU: "+message);output=[];active._cpuImages++;return false;
            }
            RunLimits.Check();active._gpuImages++;return true;
        }
    }

    public static ComputeBenchmark Benchmark(bool allowSoftware=false)
    {IntPtr context=IntPtr.Zero;try{return Measure(allowSoftware,out context);}finally{if(context!=IntPtr.Zero)pgl_destroy(context);}}
    private static ComputeBenchmark Measure(bool allowSoftware,out IntPtr context)
    {
        context=IntPtr.Zero;var message=new StringBuilder(1024);
        if(!OperatingSystem.IsLinux())return new(false,"CPU",false,false,0,0,0,"The optional Vulkan backend is packaged for Linux.");
        try { context=pgl_create(message,message.Capacity,out int software);
            if(context==IntPtr.Zero)return new(false,message.ToString(),false,false,0,0,0,"Vulkan unavailable; CPU fallback.");
            string device=message.ToString();
            if(software!=0 && !allowSoftware)return new(true,device,true,false,0,0,0,"Software Vulkan is not a GPU acceleration device.");
            const int size=512;var pixels=new byte[size*size*4];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {int i=(y*size+x)*4;pixels[i]=(byte)(x/2);pixels[i+1]=(byte)(y/2);pixels[i+2]=(byte)((x^y)&255);pixels[i+3]=255;}
            // Warm both implementations; measured Vulkan time includes allocation, upload, dispatch and readback.
            _=Bc7Encoder.EncodeImageCpu(pixels,32,32);
            var encoded=new byte[size*size];
            if(pgl_encode(context,pixels,size,size,encoded,message,message.Capacity)==0)return new(true,device,software!=0,false,0,0,0,message.ToString());
            var watch=Stopwatch.StartNew();var cpu=Bc7Encoder.EncodeImageCpu(pixels,size,size);double cpuMs=watch.Elapsed.TotalMilliseconds;
            watch.Restart();if(pgl_encode(context,pixels,size,size,encoded,message,message.Capacity)==0)return new(true,device,software!=0,false,cpuMs,0,0,message.ToString());
            double gpuMs=watch.Elapsed.TotalMilliseconds,error=0;Span<byte> a=stackalloc byte[64],b=stackalloc byte[64];
            for(int i=0;i<encoded.Length;i+=16)
            {
                Bc7Decoder.DecodeBlock(cpu.AsSpan(i,16),a);Bc7Decoder.DecodeBlock(encoded.AsSpan(i,16),b);
                for(int j=0;j<64;j++){double delta=a[j]-b[j];error+=delta*delta;}
            }
            error/=pixels.Length;bool passed=error<=16;
            string recommendation=software!=0 && passed?"Software Vulkan quality check passed; timings are validation only, and automatic mode keeps CPU.":!passed?"GPU quality check failed; CPU fallback.":gpuMs<cpuMs*.8?"Measured Vulkan speedup; automatic mode may use it.":"No measured speedup on this workload; automatic mode keeps CPU.";
            return new(true,device,software!=0,passed,cpuMs,gpuMs,error,recommendation);
        }
        catch(Exception e) when(e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {return new(false,"CPU",false,false,0,0,0,"Optional Vulkan library is unavailable: "+e.GetType().Name);}
    }
    public void Dispose()
    {
        lock(_gate){if(_disposed)return;_disposed=true;if(_context!=IntPtr.Zero){pgl_destroy(_context);_context=IntPtr.Zero;}
            if(_attempted)_progress?.Report($"Texture compute finished: {_gpuImages:N0} Vulkan images, {_cpuImages:N0} CPU images.");}
        Active.Value=_previous;
    }
    [DllImport("pengulod_compute",CallingConvention=CallingConvention.Cdecl)]private static extern IntPtr pgl_create(StringBuilder message,int capacity,out int software);
    [DllImport("pengulod_compute",CallingConvention=CallingConvention.Cdecl)]private static extern void pgl_destroy(IntPtr context);
    [DllImport("pengulod_compute",CallingConvention=CallingConvention.Cdecl)]private static extern int pgl_encode(IntPtr context,byte[] pixels,uint width,uint height,byte[] output,StringBuilder message,int capacity);
}
