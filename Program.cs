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
    public const int WIDTH = 1280;
    public const int HEIGHT = 720;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Sphere
    {
      public float Radius;
      public float X;
      public float Y;
      public float Z;

      public Sphere(float radius, float x, float y, float z)
      {
        Radius = radius; X = x; Y = y; Z = z;
      }
    }

    public const uint AFLAGS_SIMPLE = ((uint)1) << 31;
    public const uint AFLAGS_SOLID = ((uint)1) << 30;
    public const uint AFLAGS_ANIMATED = ((uint)1) << 29; // animated -> skip render during static pass

    //[StructLayout(LayoutKind.Explicit)]
    public struct Voxel
    {
      //[FieldOffset(0)]
      public uint FTL;
      //[FieldOffset(4)]
      public uint FTR;
      //[FieldOffset(8)]
      public uint FBL;
      //[FieldOffset(12)]
      public uint FBR;
      //[FieldOffset(16)]
      public uint BTL;
      //[FieldOffset(20)]
      public uint BTR;
      //[FieldOffset(24)]
      public uint BBL;
      //[FieldOffset(28)]
      public uint BBR;

      //[FieldOffset(0)]
      public uint AFlags;
      //[FieldOffset(4)]
      public uint MaterialIdx;
      //[FieldOffset(8)]
      public uint MaterialParam;
      //[FieldOffset(12)]
      public uint LightingIdx; // index into voxel lighting data array
      //[FieldOffset(16)]
      public uint ObjectId; // index of object this voxel is part of
      //[FieldOffset(20)]
      public uint Reserved1;
      //[FieldOffset(24)]
      public uint Reserved2;
      //[FieldOffset(28)]
      public uint Reserved3;
    }

    public static List<uint> voxelToUint(Voxel v)
    {
      var l = new List<uint>();

      l.Add(v.AFlags | v.FTL);
      l.Add(v.MaterialIdx | v.FTR);
      l.Add(v.MaterialParam | v.FBL);
      l.Add(v.LightingIdx | v.FBR);
      l.Add(v.ObjectId | v.BTL);
      l.Add(v.Reserved1 | v.BTR);
      l.Add(v.Reserved2 | v.BBL);
      l.Add(v.Reserved3 | v.BBR);

      return l;
    }

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
      Console.ReadLine();

      var kernel = clp.CreateKernel("helloVoxel");

      int[] message = new int[] { 1, 2, 3 };
      var gcMessage = GCHandle.Alloc(message);
      ComputeBuffer<int> buffer = new ComputeBuffer<int>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.UseHostPointer, message);

      var sph = new Sphere(100, -200, 0, 150);
      var sph2 = new Sphere(100, 100, -100, 150);

      //ComputeBuffer<Sphere> sphB = new ComputeBuffer<Sphere>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, 1, );
      //ComputeBuffer<Sphere> sph2B = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, sph2);

      ComputeImage2D outimg = new ComputeImage2D(context, ComputeMemoryFlags.WriteOnly | ComputeMemoryFlags.AllocateHostPointer, new ComputeImageFormat(ComputeImageChannelOrder.Rgba, ComputeImageChannelType.UnsignedInt8), WIDTH, HEIGHT, 0, IntPtr.Zero);

      // one voxel, bounds -256 to 256
      // 100 radius sphere in this voxel, pos 0,0,128
      var sp = new Sphere(256, 0, 0, 128);
      //uint bigvox = makeVoxelForSphere(sph, -256, -256, -256, 512);
      uint bigvox = makeSampleCubel();

      /*Voxel[] voxes = voxels.ToArray();
      ComputeBuffer<Voxel> voxesBuffer = new ComputeBuffer<Voxel>(context, ComputeMemoryFlags.ReadWrite | ComputeMemoryFlags.CopyHostPointer, voxes);*/

      List<uint> lui = new List<uint>();
      for (int i = 0; i < voxels.Count; i++)
        lui.AddRange(voxelToUint(voxels[i]));
      uint[] luit = lui.ToArray();

      ComputeBuffer<uint> uintBuffer = new ComputeBuffer<uint>(context, ComputeMemoryFlags.ReadWrite | ComputeMemoryFlags.CopyHostPointer, luit);

      kernel.SetMemoryArgument(0, uintBuffer);
      kernel.SetValueArgument(1, bigvox);

      //kernel.SetValueArgument<Sphere>(0, sph);
      //kernel.SetValueArgument<Sphere>(1, sph2);
      //kernel.SetMemoryArgument(0, sphB);
      //kernel.SetMemoryArgument(1, sph2B);
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

    static uint makeSampleCubel()
    {
      // add big voxel
      Voxel v = new Voxel(); // 0
      Voxel empty = new Voxel(); // 1
      empty.AFlags = AFLAGS_SIMPLE;
      Voxel full = new Voxel(); // 2
      full.AFlags = AFLAGS_SIMPLE | AFLAGS_SOLID;


      v.AFlags = 0;
      v.FTL = 1;
      v.FTR = 1;
      v.FBL = 1;
      v.BTL = 1;
      v.BTR = 1;
      v.BBL = 1;
      v.BBR = 1;
      Voxel v2 = new Voxel(); // 3
      v.FBR = 3;

      v2.AFlags = 0;
      v2.FTL = 1;
      v2.FTR = 1;
      v2.FBL = 1;
      v2.FBR = 1;
      v2.BTL = 2;
      v2.BTR = 1;
      v2.BBL = 1;
      v2.BBR = 2;
      voxels.Add(v);
      voxels.Add(empty);
      voxels.Add(full);
      voxels.Add(v2);
      return 0;
    }

    static List<Voxel> voxels = new List<Voxel>();

    static uint makeVoxelForSphere(Sphere sph, int xmin, int ymin, int zmin, int vsize)
    {
      int xmax = xmin + vsize;
      int ymax = ymin + vsize;
      int zmax = zmin + vsize;
      int xmid = (xmax - xmin) / 2 + xmin;
      int ymid = (ymax - ymin) / 2 + ymin;
      int zmid = (zmax - zmin) / 2 + zmin;

      // because sphere, we can just sample at each corner point
      /*bool pxlylzl = pointInSphere(sph, xmin, ymin, zmin);
      bool pxmylzl = pointInSphere(sph, xmax, ymin, zmin);
      bool pxlymzl = pointInSphere(sph, xmin, ymax, zmin);
      bool pxmymzl = pointInSphere(sph, xmax, ymax, zmin);
      bool pxlylzm = pointInSphere(sph, xmin, ymin, zmax);
      bool pxmylzm = pointInSphere(sph, xmax, ymin, zmax);
      bool pxlymzm = pointInSphere(sph, xmin, ymax, zmax);
      bool pxmymzm = pointInSphere(sph, xmax, ymax, zmax);*/

      /*bool[] samples = new bool[] {
        pointInSphere(sph, xmin, ymin, zmin),
        pointInSphere(sph, xmax, ymin, zmin),
        pointInSphere(sph, xmin, ymax, zmin),
        pointInSphere(sph, xmax, ymax, zmin),
        pointInSphere(sph, xmin, ymin, zmax),
        pointInSphere(sph, xmax, ymin, zmax),
        pointInSphere(sph, xmin, ymax, zmax),
        pointInSphere(sph, xmax, ymax, zmax),
        pointInSphere(sph, xmid, ymid, zmid)
      };*/

      List<bool> samples = new List<bool>();

      var r = new Random();
      for (int i = 0; i < (vsize / 10 < 4 ? 4 : vsize / 10); i++)
        samples.Add(pointInSphere(sph, r.Next(xmin, xmax), r.Next(ymin, ymax), r.Next(zmin, zmax)));

      // did all samples hit, none, or some?
      int hit = samples.Count(b => b);

      if (hit == 0)
      {
        Voxel vempty = new Voxel();
        vempty.AFlags = AFLAGS_SIMPLE;
        voxels.Add(vempty);
        return (uint) voxels.Count - 1;
      }
      else if (hit == samples.Count)
      {
        Voxel vsolid = new Voxel();
        vsolid.AFlags = AFLAGS_SIMPLE | AFLAGS_SOLID;
        voxels.Add(vsolid);
        return (uint) voxels.Count - 1;
      }
      else if (vsize == 1)
      {
        // if vsize is 1, we can't go any less so we need to make an approximation.
        Voxel vapprox = new Voxel();
        vapprox.AFlags = AFLAGS_SIMPLE | (hit > 4 ? AFLAGS_SOLID : 0);
        voxels.Add(vapprox);
        return (uint) voxels.Count - 1;
      }
      else
      {
        // recurse down & make subvoxels.
        Voxel v = new Voxel();
        v.AFlags = 0;
        int vs = vsize / 2;

        v.FTL = makeVoxelForSphere(sph, xmin, ymin, zmin, vs);
        v.FTR = makeVoxelForSphere(sph, xmid, ymin, zmin, vs);
        v.FBL = makeVoxelForSphere(sph, xmin, ymid, zmin, vs);
        v.FBR = makeVoxelForSphere(sph, xmid, ymid, zmin, vs);

        v.BTL = makeVoxelForSphere(sph, xmin, ymin, zmid, vs);
        v.BTR = makeVoxelForSphere(sph, xmid, ymin, zmid, vs);
        v.BBL = makeVoxelForSphere(sph, xmin, ymid, zmid, vs);
        v.BBR = makeVoxelForSphere(sph, xmid, ymid, zmid, vs);

        voxels.Add(v);
        uint vi = (uint) voxels.Count - 1;
        return vi;
      }
    }

    static bool pointInSphere(Sphere sph, int x, int y, int z)
    {
      // get point in terms of sphere coords
      float px = x - sph.X;
      float py = y - sph.Y;
      float pz = z - sph.Z;

      // is length of p vector less than or equal to sphere radius?
      return (Math.Sqrt(px * px + py * py + pz * pz) <= sph.Radius);
    }

  }

}
