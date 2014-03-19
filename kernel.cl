#define RADIUS 0
#define X 1
#define Y 2
#define Z 3

float sphereHit(global float* s, float ox1, float oy1)
{
  float dx = ox1 - s[X];
  float dy = oy1 - s[Y];
  
  if (dx * dx + dy * dy < s[RADIUS] * s[RADIUS]) {
    float dz = sqrt(s[RADIUS] * s[RADIUS] - dx * dx - dy * dy);
    return dz + s[Z];
  }
  
  return 2e10f;
}

float interSphere(global float* s1, global float* s2, float minBlobRadius, float boost, float ox1, float oy1)
{
  // draw line between spheres
  float lx = s2[X] - s1[X];
  float ly = s2[Y] - s1[Y];
  float incline = ly / lx; // !!! divZero

  float x1 = ox1 - s1[X];
  float y1 = oy1 - s1[Y];
  
  // find point on line for ray
  float rx = x1;
  float ry = ly + (x1 - lx) * incline;

  float linprog = sqrt(x1 * x1 + y1 * y1) / sqrt(lx * lx + ly * ly);

  if (linprog < 0.0f)
    return 2e10f;
  if (linprog > 1.0f)
    return 2e10f;

  float s1ra = s1[RADIUS] - minBlobRadius;
  float s2ra = s2[RADIUS] - minBlobRadius;
  float lap = minBlobRadius;

  float prog = 1.0f + sin(M_PI + linprog * M_PI);
  lap += prog * (linprog > 0.5f ? s2ra : s1ra) * boost;

  if ((ry + s1[Y] - oy1) < lap && (ry + s1[Y] - oy1) > -lap)
    return sqrt(s1[RADIUS] * s1[RADIUS] - (x1 - lx / 2.0f) * (x1 - lx / 2.0f) - (y1 - ly / 2.0f) * (y1 - ly / 2.0f));

  return 2e10f;
}

float getIntensityForPixel(global float* s, global float* s2, int x, int y)
{
  float f = clamp((300.0f - sphereHit(s, x - WIDTH_DIV_2, y - HEIGHT_DIV_2)) / 300.0f, 0.0f, 1.0f);
  f += clamp((300.0f - sphereHit(s2, x - WIDTH_DIV_2, y - HEIGHT_DIV_2)) / 300.0f, 0.0f, 1.0f);

  if (f <= 0.01f)
    f = clamp((300.0f - interSphere(s, s2, 20, 1.0f, x - WIDTH_DIV_2, y - HEIGHT_DIV_2)) / 300.0f, 0.0f, 1.0f);

  return f;
}

kernel void helloWorld(global float* s, global float* s2, write_only image2d_t outimg)
{
  float f;
  uchar intensity;
  uchar r;
  uchar g;
  uchar b;
  uint4 outc;

  size_t x = get_global_id(0);
  size_t y = get_global_id(1);

  /*for (int y = 0; y < HEIGHT; y++) {
    for (int x = 0; x < WIDTH; x++) {*/
      f = clamp(getIntensityForPixel(s, s2, x, y), 0.0f, 1.0f);

      // float -> uchar
      intensity = (uchar) (255.0f * f);
      r = intensity; g = intensity; b = intensity;

      // do we need a BGRA or RGBA image?
#ifdef BGRA
      outc = (uint4) (b, g, r, 255);
#else
      outc = (uint4) (r, g, b, 255);
#endif

      write_imageui(outimg, (int2)(x, y), outc);
    /*}
  }*/
}

