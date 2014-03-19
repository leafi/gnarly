using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cloo;
using Cloo.Bindings;
using System.IO;
using System.Runtime.InteropServices;

namespace gnarly
{
  class Program
  {
    public const int RADIUS = 0;
    public const int X = 1;
    public const int Y = 2;
    public const int Z = 3;

    public const int WIDTH = 1920;
    public const int HEIGHT = 1080;

    public static void Main(string[] args)
    {
      // config here!

      foreach (var p in ComputePlatform.Platforms)
        Console.WriteLine("Platform ven " + p.Vendor + " ver " + p.Version + " n " + p.Name + " prof " + p.Profile + " with " + p.Devices.Count + " devices");

      Console.WriteLine("Picking first platform. If you need a different one, or a non-GPU, this code needs changin'.");

      var platform = ComputePlatform.Platforms[0];


      var d = new ComputeContextNotifier((s, t, u, v)  => Console.WriteLine(s));
      ComputeContext context = new ComputeContext(ComputeDeviceTypes.Gpu, new ComputeContextPropertyList(ComputePlatform.Platforms[0]), d, IntPtr.Zero);
      
      Console.WriteLine();
      Console.WriteLine("GPU Devices in Platform 0: ");
      foreach (var dev in context.Devices)
        Console.WriteLine(" - " + dev.Vendor + " " + dev.Name + "\n     global memory " + (dev.GlobalMemorySize / (1024 * 1024)) + "M\n     local memory " + (dev.LocalMemorySize / (1024)) + "K\n     local is global? " + (dev.LocalMemoryType == ComputeDeviceLocalMemoryType.Global));

      Console.WriteLine("\nPicking first device. If this is bad, this code needs changing.\n");

      var device = context.Devices[0];
      Console.WriteLine("max const buffer size " + device.MaxConstantBufferSize);
      Console.WriteLine("max const buffer args " + device.MaxConstantArguments);
      Console.WriteLine("max mem alloc size " + device.MaxMemoryAllocationSize);
      Console.WriteLine("max params size " + device.MaxParameterSize);
      Console.WriteLine("max work item dimensions " + device.MaxWorkItemDimensions);
      for (int i = 0; i < device.MaxWorkItemSizes.Count; i++)
        Console.WriteLine("max work item dim" + i + " " + device.MaxWorkItemSizes[i]);

      ComputeCommandQueue queue = new ComputeCommandQueue(context, context.Devices[0], ComputeCommandQueueFlags.None);

      var cl = File.ReadAllText("../../../kernel.cl");
      ComputeProgram clp = new ComputeProgram(context, cl);
      try
      {
        var plat = Environment.OSVersion.Platform;
        var win = (plat == PlatformID.Win32NT || plat == PlatformID.Win32Windows);
        clp.Build(null, (win ? "-DBGRA " : null) + " -DWIDTH=" + WIDTH + " -DWIDTH_DIV_2=" + (WIDTH / 2) + " -DHEIGHT=" + HEIGHT + " -DHEIGHT_DIV_2=" + (HEIGHT / 2), null, IntPtr.Zero);
      }
      catch (Exception e)
      {
        Console.WriteLine("### KERNEL.CL BUILD FAILURE");
        Console.WriteLine(clp.GetBuildLog(device));
        Console.ReadLine();
        return;
      }
      
      Console.WriteLine(clp.GetBuildLog(device));

      var kernel = clp.CreateKernel("helloWorld");

      int[] message = new int[] { 1, 2, 3 };
      var gcMessage = GCHandle.Alloc(message);
      ComputeBuffer<int> buffer = new ComputeBuffer<int>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.UseHostPointer, message);

      var sph = new float[4];
      sph[X] = -200;
      sph[Y] = 0;
      sph[Z] = 150;
      sph[RADIUS] = 100;

      var sph2 = new float[4];
      sph2[X] = 100;
      sph2[Y] = -100;
      sph2[Z] = 150;
      sph2[RADIUS] = 100;

      ComputeBuffer<float> sphB = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, sph);
      ComputeBuffer<float> sph2B = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, sph2);

      ComputeImage2D outimg = new ComputeImage2D(context, ComputeMemoryFlags.WriteOnly | ComputeMemoryFlags.AllocateHostPointer, new ComputeImageFormat(ComputeImageChannelOrder.Rgba, ComputeImageChannelType.UnsignedInt8), WIDTH, HEIGHT, 0, IntPtr.Zero);

      kernel.SetMemoryArgument(0, sphB);
      kernel.SetMemoryArgument(1, sph2B);
      kernel.SetMemoryArgument(2, outimg);


      // -cl-fast-relaxed-math ?
      // & execute in parallel...
      
      //queue.ExecuteTask(kernel, null);
      queue.Execute(kernel, null, new long[] { WIDTH, HEIGHT }, null, null);
      queue.Finish();

      gcMessage.Free();



      // code goes here

      

      var f = new Form1();
      f.Show();
      f.DrawOut(outimg, queue);

      Console.ReadLine();
    }

  }
}
