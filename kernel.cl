/*#define RADIUS 0
#define X 1
#define Y 2
#define Z 3*/

typedef struct
{
  float radius;
  float x;
  float y;
  float z;
} Sphere;


typedef struct __attribute__ ((packed))
{
  uint FTL;
  uint FTR;
  uint FBL;
  uint FBR;
  uint BTL;
  uint BTR;
  uint BBL;
  uint BBR;
} OctreeVoxel;

typedef struct __attribute__ ((packed))
{
  uint AFlags;
  uint MaterialIdx;
  uint MaterialParam;
  uint LightingIdx;
  uint ObjectId;
  uint Reserved1;
  uint Reserved2;
  uint Reserved3;
} SimpleVoxel;

#define AFLAGS_SIMPLE (1 << 30)
#define AFLAGS_SOLID (1 << 29)
#define AFLAGS_ANIMATED (1 << 28)

/*typedef struct __attribute__ ((packed))
{
  union
  {
    OctreeVoxel octree;
    SimpleVoxel simple;
  };
} Voxel;*/

typedef struct
{
  uint AFlags;
  uint FTR;
  uint FBL;
  uint FBR;
  uint BTL;
  uint BTR;
  uint BBL;
  uint BBR;
} Voxel;

typedef struct
{
  float3 origin;
  float3 direction;
  float3 inv_direction;
  int3 sign;
} Ray;

void setup_ray(Ray* ray, float3 origin, float3 direction)
{
  ray->origin = origin;
  ray->direction = direction;
  float3 inv_direction = ((float3)(1.0f, 1.0f, 1.0f)) / direction;
  ray->inv_direction = inv_direction;
  ray->sign.x = (inv_direction.x < 0) ? 1 : 0;
  ray->sign.y = (inv_direction.y < 0) ? 1 : 0;
  ray->sign.z = (inv_direction.z < 0) ? 1 : 0;
}

// https://github.com/hpicgs/cgsee/wiki/Ray-Box-Intersection-on-the-GPU
void ray_aabb_intersection_distance2(Ray* ray, float3* aabb, float* tmin, float* tmax)
{
  float tymin, tymax, tzmin, tzmax;
  *tmin = (aabb[ray->sign.x].x - ray->origin.x) * ray->inv_direction.x;
  *tmax = (aabb[1-ray->sign.x].x - ray->origin.x) * ray->inv_direction.x;
  tymin = (aabb[ray->sign.y].y - ray->origin.y) * ray->inv_direction.y;
  tymax = (aabb[1-ray->sign.y].y - ray->origin.y) * ray->inv_direction.y;
  tzmin = (aabb[ray->sign.z].z - ray->origin.z) * ray->inv_direction.z;
  tzmax = (aabb[1-ray->sign.z].z - ray->origin.z) * ray->inv_direction.z;
  *tmin = max(max(*tmin, tymin), tzmin);
  *tmax = min(min(*tmax, tymax), tzmax);
  // post condition:
  // if tmin > tmax (in the code above this is represented by a return value of INFINITY)
  //     no intersection
  // else
  //     front intersection point = ray.origin + ray.direction * tmin (normally only this point matters)
  //     back intersection point  = ray.origin + ray.direction * tmax
}

/*float ray_aabb_intersection_distance2(Ray* ray, float3* aabb) {
  float tmin, tmax, tymin, tymax, tzmin, tzmax;
  tmin = (aabb[ray->sign.x].x - ray->origin.x) * ray->inv_direction.x;
  tmax = (aabb[1-ray->sign.x].x - ray->origin.x) * ray->inv_direction.x;
  tymin = (aabb[ray->sign.y].y - ray->origin.y) * ray->inv_direction.y;
  tymax = (aabb[1-ray->sign.y].y - ray->origin.y) * ray->inv_direction.y;
  if ( (tmin > tymax) || (tymin > tmax) ) return INFINITY;
  tmin = min(tmin, tymin);
  tmax = max(tmax, tymax);
  tzmin = (aabb[ray->sign.z].z - ray->origin.z) * ray->inv_direction.z;
  tzmax = (aabb[1-ray->sign.z].z - ray->origin.z) * ray->inv_direction.z;
  if ( (tmin > tzmax) || (tzmin > tmax) ) return INFINITY;
  tmin = min(tmin, tzmin);
  //tmax = max(tmay, tzmax);
  return tmin;
}*/

/*bool ray_aabb_intersection_distance(Ray* ray, float3* aabb) {
  float tmin; float tmax;
  ray_aabb_intersection_distance2(ray, aabb, &tmin, &tmax);
  return (tmin < tmax);
}*/

bool ray_aabb_intersection_distance(Ray* ray, float3* aabb) {
  // VERY BAD
  if (ray->origin.x > aabb[0].x && ray->origin.y > aabb[0].y && ray->origin.x <= aabb[1].x && ray->origin.y <= aabb[1].y) {
    return true;
  } else {
    return false;
  }
}

int getVoxelIntensityForPixelStep(Voxel* vox, Ray* ray, float3* aabb, float3* intersect, float3* backIntersect)
{
  // do we hit this voxel?
  bool tmin; float tmax;
  //ray_aabb_intersection_distance(ray, aabb, &tmin, &tmax);
  tmin = ray_aabb_intersection_distance(ray, aabb);

  if (!tmin) {
    // no intersection
    return -1;
  } else {
    //*intersect = ray->origin + ray->direction * tmin;

    // do we need to go deeper?
    if ((vox->AFlags & AFLAGS_SIMPLE) == 0) {
      // yes, yes we do.
      return 1;
    } else {
      if ((vox->AFlags & AFLAGS_SOLID) == 0) {
        // empty voxel
        return -1;
      } else {
        // solid voxel! :)
        return 0;
      }
    }
  }
}

float getVoxelIntensityForPixel(global Voxel* voxels, uint sv, float x, float y)
{
  // assume voxel bounds are -256..256 for now.
  float3 aabb[2];
  aabb[0] = (float3)(-256.0f, -256.0f, -256.0f);
  aabb[1] = (float3)(256.0f, 256.0f, 256.0f);

  // cast ray from point on screen +z
  Ray ray;
  float3 direction = (float3)(0.0f, 0.0f, 1.0f);
  //float3 direction = normalize((float3)(-0.2f, 0.0f, 1.0f));
  setup_ray(&ray, (float3)(x, y, 0.0f), direction);

  int r;

  //printf("vi for pixel %f %f vox %d\n", x, y, sv);

  // copy voxel to private memory
  Voxel voxBig = voxels[sv];
  //printf("vox AFlags/FTL %d\n", voxBig.AFlags);
  float3 intersect;
  float3 backIntersect;

  // problem: we might need to search up to 3 subvoxels to get the right answer.
  // ringbuffer lets us search multiple subvoxels.
  #define RING_LENGTH 64
  Voxel ring[RING_LENGTH];
  float3 ringAabb0[RING_LENGTH];
  float3 ringAabb1[RING_LENGTH];
  int ringFront = 0;
  int ringBack = 0;

  for (int i = 0; i < 128; i++) {
  //while (true) {
    //printf("round %d\n", i);
    // do cast
    r = getVoxelIntensityForPixelStep(&voxBig, &ray, aabb, &intersect, &backIntersect);

    if (r == 1) {
      // WE MUST GO DEEPER.
      //printf("%s", "DEEEPER\n");

      float minx = aabb[0].x;
      float maxx = aabb[1].x;
      float midx = (maxx - minx) * 0.5f + minx;
      float miny = aabb[0].y;
      float maxy = aabb[1].y;
      float midy = (maxy - miny) * 0.5f + miny;
      float minz = aabb[0].z;
      float maxz = aabb[1].z;
      float midz = (maxz - minz) * 0.5f + minz;
      float vs = (maxx - minx) * 0.5f;

      float3 testaabb[2];
      bool tmin; float tmax;
      //float3 ir;

      int c = 0;

      // TODO: sort results, add in closest order!!!!!
      
      // FTL
      //printf("%s", "hi0\n");
      testaabb[0] = (float3)(minx, miny, minz);
      testaabb[1] = (float3)(testaabb[0].x + vs, testaabb[0].y + vs, testaabb[0].z + vs);
      tmin = ray_aabb_intersection_distance(&ray, testaabb);
      //ir = ray.origin + ray.direction * tmin;
      if (tmin) {
        ring[ringBack] = voxels[voxBig.AFlags];
        ringAabb0[ringBack] = testaabb[0];
        ringAabb1[ringBack] = testaabb[1];
        ringBack = (ringBack + 1) % RING_LENGTH;
        c++;
      }

      // FTR
      //printf("%s", "hi1\n");
      testaabb[0] = (float3)(midx, miny, minz);
      testaabb[1] = (float3)(testaabb[0].x + vs, testaabb[0].y + vs, testaabb[0].z + vs);
      tmin = ray_aabb_intersection_distance(&ray, testaabb);
      //ir = ray.origin + ray.direction * tmin;
      if (tmin) {
        ring[ringBack] = voxels[voxBig.FTR];
        ringAabb0[ringBack] = testaabb[0];
        ringAabb1[ringBack] = testaabb[1];
        ringBack = (ringBack + 1) % RING_LENGTH;
        c++;
      }
 
      // FBL
      //printf("%s", "hi2\n");
      testaabb[0] = (float3)(minx, midy, minz);
      testaabb[1] = (float3)(testaabb[0].x + vs, testaabb[0].y + vs, testaabb[0].z + vs);
      tmin = ray_aabb_intersection_distance(&ray, testaabb);
      //ir = ray.origin + ray.direction * tmin;

      if (tmin) {
        ring[ringBack] = voxels[voxBig.FBL];
        ringAabb0[ringBack] = testaabb[0];
        ringAabb1[ringBack] = testaabb[1];
        ringBack = (ringBack + 1) % RING_LENGTH;
        c++;
      }

      // FBR
      //printf("%s", "hi3\n");
      testaabb[0] = (float3)(midx, midy, minz);
      testaabb[1] = (float3)(testaabb[0].x + vs, testaabb[0].y + vs, testaabb[0].z + vs);
      tmin = ray_aabb_intersection_distance(&ray, testaabb);
      //ir = ray.origin + ray.direction * tmin;
      if (tmin) {
        ring[ringBack] = voxels[voxBig.FBR];
        ringAabb0[ringBack] = testaabb[0];
        ringAabb1[ringBack] = testaabb[1];
        ringBack = (ringBack + 1) % RING_LENGTH;
        c++;
      }

      // BTL
      //printf("%s", "hi4\n");
      testaabb[0] = (float3)(minx, miny, midz);
      testaabb[1] = (float3)(testaabb[0].x + vs, testaabb[0].y + vs, testaabb[0].z + vs);
      //printf("BTL 0 %f %f %f 1 %f %f %f\n", testaabb[0].x, testaabb[0].y, testaabb[0].z, testaabb[1].x, testaabb[1].y, testaabb[1].z);
      tmin = ray_aabb_intersection_distance(&ray, testaabb);
      if (tmin) {
        ring[ringBack] = voxels[voxBig.BTL];
        ringAabb0[ringBack] = testaabb[0];
        ringAabb1[ringBack] = testaabb[1];
        ringBack = (ringBack + 1) % RING_LENGTH;
        c++;
      }

      // BTR
      //printf("%s", "hi5\n");
      testaabb[0] = (float3)(midx, miny, midz);
      testaabb[1] = (float3)(testaabb[0].x + vs, testaabb[0].y + vs, testaabb[0].z + vs);
      tmin = ray_aabb_intersection_distance(&ray, testaabb);
      if (tmin) {
        ring[ringBack] = voxels[voxBig.BTR];
        ringAabb0[ringBack] = testaabb[0];
        ringAabb1[ringBack] = testaabb[1];
        ringBack = (ringBack + 1) % RING_LENGTH;
        c++;
      }

      // BBL
      testaabb[0] = (float3)(minx, midy, midz);
      testaabb[1] = (float3)(testaabb[0].x + vs, testaabb[0].y + vs, testaabb[0].z + vs);
      tmin = ray_aabb_intersection_distance(&ray, testaabb);
      if (tmin) {
        ring[ringBack] = voxels[voxBig.BBL];
        ringAabb0[ringBack] = testaabb[0];
        ringAabb1[ringBack] = testaabb[1];
        ringBack = (ringBack + 1) % RING_LENGTH;
        c++;
      }

      // BBR
      testaabb[0] = (float3)(midx, midy, midz);
      testaabb[1] = (float3)(testaabb[0].x + vs, testaabb[0].y + vs, testaabb[0].z + vs);
      tmin = ray_aabb_intersection_distance(&ray, testaabb);
      if (tmin) {
        ring[ringBack] = voxels[voxBig.BBR];
        ringAabb0[ringBack] = testaabb[0];
        ringAabb1[ringBack] = testaabb[1];
        ringBack = (ringBack + 1) % RING_LENGTH;
        c++;
      }
      
      
      /*int hack = 0;
      int bees = 1;
      // front or back?
      if (intersect.z > midz) {
        // back!
        bees = 0;
        hack = 4;
      }
      aabb[bees].z = midz;
      bees = 1;

      // top or bottom?
      if (intersect.y > midy) {
        // bottom!
        bees = 0;
        hack += 2;
      }
      aabb[bees].y = midy;
      bees = 1;

      // left or right?
      if (intersect.x > midx) {
        // right!
        bees = 0;
        hack++;
      }
      aabb[bees].x = midx;
      bees = 1;
      
      int nextIdx = ((uint*)&voxBig)[hack];*/
      //printf("DEEPER to %d (px %f %f) \n    (aabb0 %f %f %f aabb1 %f %f %f) \n    (intersect %f %f %f)\n", nextIdx, x, y, aabb[0].x, aabb[0].y, aabb[0].z, aabb[1].x, aabb[1].y, aabb[1].z, intersect.x, intersect.y, intersect.z);
      

      // XXX ray can intersect up to 3 voxels, and we might need to investigate ALL of them.
      // for now, just skip the front result if it is simple and empty.
      // (i think for accurate rendering we need to check the ray against the bounding box of each
      //  subvoxel, find the 3 it could be, and add them to the ring buffer to be checked.)
      // ((will that be slow?  NO idea.  i think i need better data before i can start checking that!))
      
      

      /*ring[ringBack] = voxels[nextIdx];
      ringAabb0[ringBack] = aabb[0];
      ringAabb1[ringBack] = aabb[1];

      ringBack++;
      if (ringBack >= RING_LENGTH) {
        ringBack = 0;
      }*/

    } else if (r == 0) {
      // Simple! :)
      //printf("%s", "BRIGHT\n");


      // Let's light this bad boy!
      float3 lightPosition = (float3)(100.0f, -100.0f, -200.0f);
      float3 diffuseColor = (float3)(1.0f, 1.0f, 1.0f);
      float diffusePower = 100000.0f;
      // blah blah todo specular

      // from voxel
      float3 voxPosition = aabb[0]; // TODO: MAKE BETTER
      voxPosition.x = x;
      voxPosition.y = y;
      float3 viewDir = (float3)(0.0f, 0.0f, 1.0f);
      // sphere hack. should be stored on voxel or summat!
      float3 normal = normalize(voxPosition - (float3)(0.0f, 0.0f, 128.0f));

      float3 lightDir = lightPosition - voxPosition;
      float distance = length(lightDir);
      lightDir = lightDir / distance;
      distance = distance * distance;

      float nDotL = dot(normal, lightDir);
      float intensity = clamp(nDotL, 0.0f, 1.0f);

      return 0.5f + intensity * diffuseColor.x * diffusePower / distance;
    }

    // no result?
    // pull from ring buffer
    if (ringFront != ringBack) {
      voxBig = ring[ringFront];
      aabb[0] = ringAabb0[ringFront];
      aabb[1] = ringAabb1[ringFront];

      ringFront++;
      if (ringFront >= RING_LENGTH) {
        ringFront = 0;
      }
    } else {
      // out of candidates? then we're done.
      //printf("%s", "out of candidates\n");
      return 0.0f;
    }

  } // end for

  // uhhh, we ran out of guesses before we got to the answer.
  // let's....... guess.
  //printf("%s", "DUNNO\n");
  return 0.5f;

} // end getVoxelIntensityForPixel

kernel void helloVoxel(global Voxel* voxels, uint svox, write_only image2d_t outimg)
{
  float f;
  uchar intensity;
  uchar r;
  uchar g;
  uchar b;
  uint4 outc;

  size_t x = get_global_id(0);
  size_t y = get_global_id(1);

  f = clamp(getVoxelIntensityForPixel(voxels, svox, (float)x - (float)WIDTH_DIV_2, (float)y - (float)HEIGHT_DIV_2), 0.0f, 1.0f);

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
}

float sphereHit(Sphere* s, float ox1, float oy1)
{
  float dx = ox1 - s->x;
  float dy = oy1 - s->y;
  
  if (dx * dx + dy * dy < s->radius * s->radius) {
    float dz = sqrt(s->radius * s->radius - dx * dx - dy * dy);
    return dz + s->z;
  }
  
  return 2e10f;
}

float interSphere(Sphere* s1, Sphere* s2, float minBlobRadius, float boost, float ox1, float oy1)
{
  // draw line between spheres
  float lx = s2->x - s1->x;
  float ly = s2->y - s1->y;
  float incline = ly / lx; // !!! divZero

  float x1 = ox1 - s1->x;
  float y1 = oy1 - s1->y;
  
  // find point on line for ray
  float rx = x1;
  float ry = ly + (x1 - lx) * incline;

  float linprog = sqrt(x1 * x1 + y1 * y1) / sqrt(lx * lx + ly * ly);

  if (linprog < 0.0f)
    return 2e10f;
  if (linprog > 1.0f)
    return 2e10f;

  float s1ra = s1->radius - minBlobRadius;
  float s2ra = s2->radius - minBlobRadius;
  float lap = minBlobRadius;

  float prog = 1.0f + sin(M_PI + linprog * M_PI);
  lap += prog * (linprog > 0.5f ? s2ra : s1ra) * boost;

  if ((ry + s1->y - oy1) < lap && (ry + s1->y - oy1) > -lap)
    return sqrt(s1->radius * s1->radius - (x1 - lx / 2.0f) * (x1 - lx / 2.0f) - (y1 - ly / 2.0f) * (y1 - ly / 2.0f));

  return 2e10f;
}

float getIntensityForPixel(Sphere* s, Sphere* s2, int x, int y)
{
  float f = clamp((300.0f - sphereHit(s, x - WIDTH_DIV_2, y - HEIGHT_DIV_2)) / 300.0f, 0.0f, 1.0f);
  f += clamp((300.0f - sphereHit(s2, x - WIDTH_DIV_2, y - HEIGHT_DIV_2)) / 300.0f, 0.0f, 1.0f);

  if (f <= 0.01f)
    f = clamp((300.0f - interSphere(s, s2, 20, 1.0f, x - WIDTH_DIV_2, y - HEIGHT_DIV_2)) / 300.0f, 0.0f, 1.0f);

  return f;
}

kernel void helloWorld(Sphere s, Sphere s2, write_only image2d_t outimg)
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
      f = clamp(getIntensityForPixel(&s, &s2, x, y), 0.0f, 1.0f);

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

