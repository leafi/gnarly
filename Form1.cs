using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace gnarly
{
  public partial class Form1 : Form
  {
    public Form1()
    {
      InitializeComponent();
    }

    bool weManualNow = false;
    int bmpLen = 0;

    public void DrawOut(Cloo.ComputeImage2D outimg, Cloo.ComputeCommandQueue queue, int xi = -1, int yi = -1)
    {
      this.Size = new Size(outimg.Width, outimg.Height);

      var g = this.CreateGraphics();

      var bmp = new Bitmap(outimg.Width, outimg.Height);
      var bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);

      if (!weManualNow)
        bd.Scan0 = Marshal.AllocHGlobal((int) outimg.Size);
      bmpLen = (int) outimg.Size;
      weManualNow = true;
      
      bd.Stride = (int) outimg.RowPitch;

      //IntPtr ptr = Marshal.AllocHGlobal((int) outimg.Size);
      queue.ReadFromImage(outimg, bd.Scan0, true, null);
      queue.Finish();

      /*for (int y = 0; y < bmp.Height; y++) {
        for (int x = 0; x < bmp.Height; x++) {
          unsafe {
            byte* xsSrc = (byte*)(ptr.ToInt64() + y * outimg.RowPitch + x * 4);
            byte* xsDst = (byte*) (bd.Scan0.ToInt64() + y * bd.Stride + x * 4);
            *xsDst = *xsSrc;
            xsSrc++; xsDst++;
            if (x == xi || y == yi)
              *xsDst = 255;
            else
              *xsDst = *xsSrc;
            xsSrc++; xsDst++;
            *xsDst = *xsSrc;
            xsSrc++; xsDst++;
            *xsDst = *xsSrc;
          }
        }
      }*/


      if (xi > -1 && yi > -1) {
        if (xi >= outimg.Width || yi >= outimg.Height) {
          Console.WriteLine("Out of rendered image bounds; can't probe result colour.");
        } else {
          //long ip = ptr.ToInt64();
          long ip = bd.Scan0.ToInt64();
          ip += yi * outimg.RowPitch + xi * 4;
          byte[] bs = new byte[4];
          Marshal.Copy(new IntPtr(ip), bs, 0, 4);
          Console.WriteLine("byte inspection of outbitmap: " + bs[0] + " " + bs[1] + " " + bs[2] + " " + bs[3]);
        }
      }

      //Marshal.FreeHGlobal(ptr);

      bmp.UnlockBits(bd);
      bd = null;

      if (xi > -1 && yi > -1 && xi < outimg.Width && yi < outimg.Height) {
        var ccc = bmp.GetPixel(xi, yi);
        Console.WriteLine("bmp inspection: " + ccc.R + " " + ccc.G + " " + ccc.B + " " + ccc.A);
      }

      g.DrawImageUnscaled(bmp, Point.Empty);
      g.Flush();
      g.Dispose();
    }
  }
}
