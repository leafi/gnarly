﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cloo;
using Cloo.Bindings;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Threading;

namespace gnarly
{
  class Program
  {
    public const int WIDTH = 640;
    public const int HEIGHT = 480;

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

    public const int AFLAGS_SIMPLE = 1 << 30;
    public const int AFLAGS_SOLID = 1 << 29;
    public const int AFLAGS_ANIMATED = 1 << 28; // animated -> skip render during static pass

    //[StructLayout(LayoutKind.Explicit)]
    public struct Voxel
    {
      //[FieldOffset(0)]
      public int FTL;
      //[FieldOffset(4)]
      public int FTR;
      //[FieldOffset(8)]
      public int FBL;
      //[FieldOffset(12)]
      public int FBR;
      //[FieldOffset(16)]
      public int BTL;
      //[FieldOffset(20)]
      public int BTR;
      //[FieldOffset(24)]
      public int BBL;
      //[FieldOffset(28)]
      public int BBR;

      //[FieldOffset(0)]
      public int AFlags;
      //[FieldOffset(4)]
      public int MaterialIdx;
      //[FieldOffset(8)]
      public int MaterialParam;
      //[FieldOffset(12)]
      public int LightingIdx; // index into voxel lighting data array
      //[FieldOffset(16)]
      public int ObjectId; // index of object this voxel is part of
      //[FieldOffset(20)]
      public int Reserved1;
      //[FieldOffset(24)]
      public int Reserved2;
      //[FieldOffset(28)]
      public int Reserved3;
    }

    public static List<int> voxelToUint(Voxel v)
    {
      var l = new List<int>();

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

      var kernel = clp.CreateKernel("helloVoxel");

      int[] message = new int[] { 1, 2, 3 };
      var gcMessage = GCHandle.Alloc(message);
      ComputeBuffer<int> buffer = new ComputeBuffer<int>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.UseHostPointer, message);


      ComputeImage2D outimg = new ComputeImage2D(context, ComputeMemoryFlags.WriteOnly | ComputeMemoryFlags.AllocateHostPointer, new ComputeImageFormat(ComputeImageChannelOrder.Rgba, ComputeImageChannelType.UnsignedInt8), WIDTH, HEIGHT, 0, IntPtr.Zero);

      // one voxel, bounds -256 to 256
      // 100 radius sphere in this voxel, pos 0,0,128
      var sp = new Sphere(128, 0, 0, 128);
      int bigvox = makeVoxelForSphere(sp, -256, -256, -256, 512);
      Console.WriteLine("Voxel depth: " + countVoxelDepth(bigvox));
      //int bigvox = makeSampleCubel();

      /*Voxel[] voxes = voxels.ToArray();
      ComputeBuffer<Voxel> voxesBuffer = new ComputeBuffer<Voxel>(context, ComputeMemoryFlags.ReadWrite | ComputeMemoryFlags.CopyHostPointer, voxes);*/

      List<int> lui = new List<int>();
      for (int i = 0; i < voxels.Count; i++)
        lui.AddRange(voxelToUint(voxels[i]));
      int[] luit = lui.ToArray();
      GCHandle.Alloc(luit);

      ComputeBuffer<int> intBuffer = new ComputeBuffer<int>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.AllocateHostPointer, lui.Count * 4);
      

      /*IntPtr ptr = Marshal.AllocHGlobal(lui.Count * 4);
      Marshal.Copy(luit, 0, ptr, luit.Length);*/
      // TODO: free!
      /*ComputeErrorCode cec;
      CLMemoryHandle clmh = Cloo.Bindings.CL11.CreateBuffer(context.Handle, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.UseHostPointer, new IntPtr(lui.Count * 4), ptr, out cec);
      if (cec != ComputeErrorCode.Success)
      {
      }*/

      IntPtr mapped = queue.Map<int>(intBuffer, true, ComputeMemoryMappingFlags.Write, 0, lui.Count * 4, null);
      queue.Finish();

      Marshal.Copy(luit, 0, mapped, luit.Length);

      queue.Unmap(intBuffer, ref mapped, null);
      queue.Finish();

      kernel.SetMemoryArgument(0, intBuffer);
      //Cloo.Bindings.CL11.SetKernelArg(kernel.Handle, 0, new IntPtr(lui.Count * 4), ptr);
      kernel.SetValueArgument(1, bigvox);

      //kernel.SetValueArgument<Sphere>(0, sph);
      //kernel.SetValueArgument<Sphere>(1, sph2);
      //kernel.SetMemoryArgument(0, sphB);
      //kernel.SetMemoryArgument(1, sph2B);
      kernel.SetMemoryArgument(2, outimg);


      // -cl-fast-relaxed-math ?
      // & execute in parallel...
      
      var f = new Form1();
      f.Show();

      //queue.ExecuteTask(kernel, null);
      while (true)
      {
        queue.Execute(kernel, null, new long[] { WIDTH, HEIGHT }, null, null);
        queue.Finish();
        f.DrawOut(outimg, queue);
        Application.DoEvents();
        Thread.Sleep(1);
        if (!f.Visible)
          return;
      }

      gcMessage.Free();



      // code goes here

      


      Console.ReadLine();
    }

    static int countVoxelDepth(int vi)
    {
      Voxel v = voxels[vi];
      int b = ((voxels[vi].AFlags & AFLAGS_SIMPLE) > 0) ? 0 : 1;
      return b + ((b == 1) ? new int[] { countVoxelDepth(v.FTL), countVoxelDepth(v.FTR), countVoxelDepth(v.FBL), countVoxelDepth(v.FBR),
        countVoxelDepth(v.BTL), countVoxelDepth(v.BTR), countVoxelDepth(v.BBL), countVoxelDepth(v.BBR) }.Max() : 0);
    }

    static int makeSampleCubel()
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

    static int makeVoxelForSphere(Sphere sph, int xmin, int ymin, int zmin, int vsize)
    {
      int xmax = xmin + vsize;
      int ymax = ymin + vsize;
      int zmax = zmin + vsize;
      int xmid = (xmax - xmin) / 2 + xmin;
      int ymid = (ymax - ymin) / 2 + ymin;
      int zmid = (zmax - zmin) / 2 + zmin;

      List<bool> samples = new List<bool>();

      var r = new Random();
      int imax = vsize / 25;
      if (imax < 5)
        imax = 5;
      for (int zi = 0; zi < imax; zi++)
        for (int yi = 0; yi < imax; yi++)
          for (int xi = 0; xi < imax; xi++)
            samples.Add(pointInSphere(sph, xmin + xi * vsize / imax, ymin + yi * vsize / imax, zmin + zi * vsize / imax));

      // did all samples hit, none, or some?
      int hit = samples.Count(b => b);

      if (hit == 0)
      {
        Voxel vempty = new Voxel();
        vempty.AFlags = AFLAGS_SIMPLE;
        voxels.Add(vempty);
        return (int) voxels.Count - 1;
      }
      else if (hit == samples.Count)
      {
        Voxel vsolid = new Voxel();
        vsolid.AFlags = AFLAGS_SIMPLE | AFLAGS_SOLID;
        voxels.Add(vsolid);
        return (int) voxels.Count - 1;
      }
      else if (vsize == 1)
      {
        // if vsize is 1, we can't go any less so we need to make an approximation.
        Voxel vapprox = new Voxel();
        //vapprox.AFlags = AFLAGS_SIMPLE | (hit > 4 ? AFLAGS_SOLID : 0);
        vapprox.AFlags = AFLAGS_SIMPLE | AFLAGS_SOLID;
        voxels.Add(vapprox);
        return (int) voxels.Count - 1;
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
        int vi = (int) voxels.Count - 1;
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
      return (Math.Abs(Math.Sqrt(px * px + py * py + pz * pz)) <= sph.Radius);
    }

  }

}
