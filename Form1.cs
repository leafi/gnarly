using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
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

    public void DrawOut(float[,] drawing)
    {
      this.Size = new Size(drawing.GetLength(0), drawing.GetLength(1));
      var g = this.CreateGraphics();

      var bmp = new Bitmap(drawing.GetLength(0), drawing.GetLength(1));
      var bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

      int y = 0;
      int x = 0;

      while (y < drawing.GetLength(1))
      {
        while (x < drawing.GetLength(0))
        {
          var d = (byte)((int)Math.Min(255f, Math.Max(0f, drawing[x, y] * 256.0f)));

          unsafe
          {
            byte* ptr = (byte*)bd.Scan0;
            ptr += bd.Stride * y;
            ptr += x * 3;

            *ptr = d;
            ptr++;
            *ptr = d;
            ptr++;
            *ptr = d;
          }
          x++;
        }
        y++;
        x = 0;
      }

      bmp.UnlockBits(bd);
      bd = null;

      g.DrawImageUnscaled(bmp, Point.Empty);
      g.Flush();
      g.Dispose();
    }

  }
}
