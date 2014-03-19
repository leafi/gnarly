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

    public void DrawOut(Cloo.ComputeImage2D outimg, Cloo.ComputeCommandQueue queue)
    {
      this.Size = new Size(outimg.Width, outimg.Height);

      var g = this.CreateGraphics();

      var bmp = new Bitmap(outimg.Width, outimg.Height);
      var bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);

      if (weManualNow && bmpLen != outimg.Size)
        Marshal.FreeHGlobal(bd.Scan0);
      
      bd.Scan0 = Marshal.AllocHGlobal((int) outimg.Size);
      bmpLen = (int) outimg.Size;
      weManualNow = true;
      
      bd.Stride = (int) outimg.RowPitch;

      queue.ReadFromImage(outimg, bd.Scan0, true, null);

      bmp.UnlockBits(bd);
      bd = null;

      g.DrawImageUnscaled(bmp, Point.Empty);
      g.Flush();
      g.Dispose();
    }
  }
}
