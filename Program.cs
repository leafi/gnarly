using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cudafy;
using Cudafy.Host;
using Cudafy.Translator;

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

    [Cudafy]
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

    public static void Main(string[] args)
    {
      // config here!
      CudafyModes.Target = eGPUType.OpenCL;
      CudafyModes.DeviceId = 0;
      CudafyTranslator.Language = CudafyModes.Target == eGPUType.OpenCL ? eLanguage.OpenCL : eLanguage.Cuda;

      CudafyModule km = CudafyTranslator.Cudafy();
      GPGPU gpu = CudafyHost.GetDevice(CudafyModes.Target, CudafyModes.DeviceId);

      gpu.LoadModule(km);

      var s = new float[4];
      s[X] = 0;
      s[Y] = 0;
      s[Z] = 150;
      s[RADIUS] = 100;

      float[,] gpuc = gpu.Allocate<float>(640, 480);

      float[] gpu_s = gpu.CopyToDevice(s);

      gpu.Launch(1, 1).thekernel(gpu_s, gpuc);

      float[,] hostc = new float[640, 480];
      gpu.CopyFromDevice(gpuc, hostc);

      var f = new Form1();
      f.Show();
      f.DrawOut(hostc);

      Console.ReadLine();
    }

    [Cudafy]
    public static void thekernel(float[] s, float[,] outc)
    {
      for (int y = 0; y < 480; y++)
        for (int x = 0; x < 640; x++)
        {
          outc[x, y] = (300f - sphereHit(s, x - 320, y - 240)) / 300f;
        }
    }
  }
}
