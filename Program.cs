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
    /*[Cudafy]
    public struct Sphere
    {
      public float radius;
      public float x;
      public float y;
      public float z;
    }*/

    public const int RADIUS = 0;
    public const int X = 1;
    public const int Y = 2;
    public const int Z = 3;

    public const int WIDTH = 640;
    public const int HEIGHT = 480;

    /*[Cudafy]
    public static float sphereHit(float[] s, float ox1, float oy1)
    {
      float dx = ox1 - s[X];
      float dy = oy1 - s[Y];

      if (dx * dx + dy * dy < s[RADIUS] * s[RADIUS])
      {
        float dz = GMath.Sqrt(s[RADIUS] * s[RADIUS] - dx * dx - dy * dy);
        return dz + s[Z];
      }

      return 2e10f;
    }

    [Cudafy]
    public static float interSphere(float[] s1, float[] s2, float minBlobRadius, float boost, float ox1, float oy1)
    {
      // draw line between spheres
      float lx = s2[X] - s1[X];
      float ly = s2[Y] - s1[Y];
      float incline = ly / lx;
      //float lz = s2[Z] - s2[Z];

      float x1 = ox1 - s1[X];
      float y1 = oy1 - s1[Y];

      // find point on line for ray
      float rx = x1;
      float ry = ly + (x1 - lx) * incline;

      //float linprog = x1 / lx;
      float linprog = GMath.Sqrt(x1 * x1 + y1 * y1) / GMath.Sqrt(lx * lx + ly * ly);

      // limit
      if (linprog < 0f)
        return 2e10f;
      if (linprog > 1f)
        return 2e10f;

      float s1ra = s1[RADIUS] - minBlobRadius;
      float s2ra = s2[RADIUS] - minBlobRadius;
      float lap = minBlobRadius;

      float prog = 1f + GMath.Sin(GMath.PI + linprog * GMath.PI);
      lap += prog * (linprog > 0.5f ? s2ra : s1ra) * boost;

      if ((ry + s1[Y] - oy1) < lap && (ry + s1[Y] - oy1) > -lap)
        return GMath.Sqrt(s1[RADIUS] * s1[RADIUS] - (x1 - lx / 2f) * (x1 - lx / 2f) - (y1 - ly / 2f) * (y1 - ly / 2f));
        //return 1f;

      // null sphere
      return 2e10f;
    }*/

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

      /*kernel.SetMemoryArgument(0, buffer);
      kernel.SetValueArgument(1, message.Length);*/




      //var maxWorkX = device.MaxWorkItemSizes[0];

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
      
      queue.ExecuteTask(kernel, null);
      queue.Finish();

      gcMessage.Free();



      // code goes here

      

      var f = new Form1();
      f.Show();
      f.DrawOut(outimg, queue);

      Console.ReadLine();
    }

    /*public static void wd3(dim3 d3)
    {
      Console.WriteLine(d3.x);
      Console.WriteLine(d3.y);
      Console.WriteLine(d3.z);
    }

    [Cudafy]
    public static float fnormalize(float f)
    {
      return GMath.Min(1f, GMath.Max(0f, f));
    }

    public const int pls = 32;

    [Cudafy]
    public static void thekernel(GThread thread, float[] s, float[] s2, float[] intersphere, float[,] outc)
    {
      int id = thread.threadIdx.x + thread.blockIdx.x * thread.blockDim.x; // *thread.gridDim.x;
      //id *= pls;
      int stride = thread.blockDim.x * thread.gridDim.x;

      if (2 * id >= 480)
        return;
      //if (id >= 640 * 480)
        //return;

      float iR = 0f; float iX = 0f; float iY = 0f; float iZ = 0f;


      for (int y = 2 * id; y < 2 * id + 2; y++)
        for (int x = 0; x < 640; x++)
        {

          //outc[x, y] = (300f - sphereHit(s, x - 320, y - 240)) / 300f;


          outc[x, y] = fnormalize((300f - sphereHit(s, x - 320, y - 240)) / 300f);
          outc[x, y] += fnormalize((300f - sphereHit(s2, x - 320, y - 240)) / 300f);

          if (outc[x, y] <= 0.01f)
            outc[x, y] = fnormalize((300f - interSphere(s, s2, 20, 1f, x - 320, y - 240)) / 300f);
          //outc[x, y] = (300f - interSphere(s, s2, 20, x - 320, y - 240

        }
    }*/
  }
}
