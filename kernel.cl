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

void setup_ray(Ray* ray, float3 origin, float3 direction);
bool ray_aabb_intersection_distance(Ray* ray, float3* aabb, float* distance);
int getVoxelIntensityForPixelStep(Voxel* vox, Ray* ray, float3* aabb, float* intersect);
float getVoxelIntensityForPixel(global Voxel* voxels, uint sv, float x, float y, float vfx, float vfy);

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
void ray_aabb_intersection_distance2(Ray* ray, float3* aabb, float* tmin, float* tmax);
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

bool ray_aabb_intersection_distance(Ray* ray, float3* aabb, float* intersect) {
  float tmin; float tmax;
  ray_aabb_intersection_distance2(ray, aabb, &tmin, &tmax);
  *intersect = tmin; //ray->origin + ray->direction * tmin;
  return (tmin <= tmax) && isfinite(tmin) && isfinite(tmax) && !isnan(tmin) && !isnan(tmax);
}

/*bool ray_aabb_intersection_distance(Ray* ray, float3* aabb) {
  // VERY BAD
  if (ray->origin.x > aabb[0].x && ray->origin.y > aabb[0].y && ray->origin.x <= aabb[1].x && ray->origin.y <= aabb[1].y) {
    return true;
  } else {
    return false;
  }
}*/

int getVoxelIntensityForPixelStep(Voxel* vox, Ray* ray, float3* aabb, float* intersect)
{
  // do we hit this voxel?
  bool tmin; float tmax;
  //ray_aabb_intersection_distance(ray, aabb, &tmin, &tmax);
  tmin = ray_aabb_intersection_distance(ray, aabb, intersect);

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

float getVoxelIntensityForPixel(global Voxel* voxels, uint sv, float x, float y, float vfx, float vfy)
{
  // assume voxel bounds are -256..256 for now.
  float3 aabb[2];
  aabb[0] = (float3)(-256.0f, -256.0f, -256.0f);
  aabb[1] = (float3)(256.0f, 256.0f, 256.0f);

  // cast ray from point on screen +z
  Ray ray;
  float3 direction = (float3)(0.0f, 0.0f, 1.0f);
  //float3 direction = normalize((float3)(-0.2f, 0.0f, 1.0f));
  //setup_ray(&ray, (float3)(x, y, 0.0f), direction);
  setup_ray(&ray, (float3)(x, y, 0.0f), normalize((float3)(vfx * x / ((float)WIDTH), vfy * y / ((float)HEIGHT), 1.0f)));

  int r;

  //printf("vi for pixel %f %f vox %d\n", x, y, sv);

  // copy voxel to private memory
  Voxel voxBig = voxels[sv];
  //printf("vox AFlags/FTL %d\n", voxBig.AFlags);
  float intersect;

  // problem: we might need to search up to 3 subvoxels to get the right answer.
  // ringbuffer lets us search multiple subvoxels.
  #define RING_LENGTH 128
  Voxel ring[RING_LENGTH];
  int ringInd[RING_LENGTH]; // debugging only
  float3 ringAabb0[RING_LENGTH];
  float3 ringAabb1[RING_LENGTH];
  int ringFront = 0;
  int ringBack = 0;

  // make sure we always return the best voxel,
  //  not the one we reach soonest.
  Voxel bestVoxel;
  float bestDistance = 100000.0f;
  float3 bestAabb0;
  float3 bestAabb1;

#if HALP
  printf("getVoxelIntensityForPixel %f %f...\n", x, y);
  printf("round 0 voxel %d xyz %f %f %f to %f %f %f\n", sv, aabb[0].x, aabb[0].y, aabb[0].z, aabb[1].x, aabb[1].y, aabb[1].z);
#endif

  for (int i = 0; i < 256; i++) {
  //while (true) {
    //printf("round %d\n", i);
    // do cast
    r = getVoxelIntensityForPixelStep(&voxBig, &ray, aabb, &intersect);

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
      tmin = ray_aabb_intersection_distance(&ray, testaabb, &intersect);
      //ir = ray.origin + ray.direction * tmin;
      if (tmin) {
        ring[ringBack] = voxels[voxBig.AFlags];
        ringInd[ringBack] = voxBig.AFlags;
        ringAabb0[ringBack] = testaabb[0];
        ringAabb1[ringBack] = testaabb[1];
        #ifdef HALP
        printf("FTL is interesting; pushing voxel %d\n", ringInd[ringBack]);
        #endif
        ringBack = (ringBack + 1) % RING_LENGTH;
        c++;
      }

      // FTR
      //printf("%s", "hi1\n");
      testaabb[0] = (float3)(midx, miny, minz);
      testaabb[1] = (float3)(testaabb[0].x + vs, testaabb[0].y + vs, testaabb[0].z + vs);
      tmin = ray_aabb_intersection_distance(&ray, testaabb, &intersect);
      //ir = ray.origin + ray.direction * tmin;
      if (tmin) {
        ring[ringBack] = voxels[voxBig.FTR];
        ringInd[ringBack] = voxBig.FTR;
        ringAabb0[ringBack] = testaabb[0];
        ringAabb1[ringBack] = testaabb[1];
        #ifdef HALP
        printf("FTR is interesting; pushing voxel %d\n", ringInd[ringBack]);
        #endif
        ringBack = (ringBack + 1) % RING_LENGTH;
        c++;
      }
 
      // FBL
      //printf("%s", "hi2\n");
      testaabb[0] = (float3)(minx, midy, minz);
      testaabb[1] = (float3)(testaabb[0].x + vs, testaabb[0].y + vs, testaabb[0].z + vs);
      tmin = ray_aabb_intersection_distance(&ray, testaabb, &intersect);
      //ir = ray.origin + ray.direction * tmin;

      if (tmin) {
        ring[ringBack] = voxels[voxBig.FBL];
        ringInd[ringBack] = voxBig.FBL;
        ringAabb0[ringBack] = testaabb[0];
        ringAabb1[ringBack] = testaabb[1];
        #ifdef HALP
        printf("FBL is interesting; pushing voxel %d\n", ringInd[ringBack]);
        #endif
        ringBack = (ringBack + 1) % RING_LENGTH;
        c++;
      }

      // FBR
      //printf("%s", "hi3\n");
      testaabb[0] = (float3)(midx, midy, minz);
      testaabb[1] = (float3)(testaabb[0].x + vs, testaabb[0].y + vs, testaabb[0].z + vs);
      tmin = ray_aabb_intersection_distance(&ray, testaabb, &intersect);
      //ir = ray.origin + ray.direction * tmin;
      if (tmin) {
        ring[ringBack] = voxels[voxBig.FBR];
        ringInd[ringBack] = voxBig.FBR;
        ringAabb0[ringBack] = testaabb[0];
        ringAabb1[ringBack] = testaabb[1];
        #ifdef HALP
        printf("FBR is interesting; pushing voxel %d\n", ringInd[ringBack]);
        #endif
        ringBack = (ringBack + 1) % RING_LENGTH;
        c++;
      }

      // BTL
      //printf("%s", "hi4\n");
      testaabb[0] = (float3)(minx, miny, midz);
      testaabb[1] = (float3)(testaabb[0].x + vs, testaabb[0].y + vs, testaabb[0].z + vs);
      //printf("BTL 0 %f %f %f 1 %f %f %f\n", testaabb[0].x, testaabb[0].y, testaabb[0].z, testaabb[1].x, testaabb[1].y, testaabb[1].z);
      tmin = ray_aabb_intersection_distance(&ray, testaabb, &intersect);
      if (tmin) {
        ring[ringBack] = voxels[voxBig.BTL];
        ringInd[ringBack] = voxBig.BTL;
        ringAabb0[ringBack] = testaabb[0];
        ringAabb1[ringBack] = testaabb[1];
        #ifdef HALP
        printf("BTL is interesting; pushing voxel %d\n", ringInd[ringBack]);
        #endif
        ringBack = (ringBack + 1) % RING_LENGTH;
        c++;
      }

      // BTR
      //printf("%s", "hi5\n");
      testaabb[0] = (float3)(midx, miny, midz);
      testaabb[1] = (float3)(testaabb[0].x + vs, testaabb[0].y + vs, testaabb[0].z + vs);
      tmin = ray_aabb_intersection_distance(&ray, testaabb, &intersect);
      if (tmin) {
        ring[ringBack] = voxels[voxBig.BTR];
        ringInd[ringBack] = voxBig.BTR;
        ringAabb0[ringBack] = testaabb[0];
        ringAabb1[ringBack] = testaabb[1];
        #ifdef HALP
        printf("BTR is interesting; pushing voxel %d\n", ringInd[ringBack]);
        #endif
        ringBack = (ringBack + 1) % RING_LENGTH;
        c++;
      }

      // BBL
      testaabb[0] = (float3)(minx, midy, midz);
      testaabb[1] = (float3)(testaabb[0].x + vs, testaabb[0].y + vs, testaabb[0].z + vs);
      tmin = ray_aabb_intersection_distance(&ray, testaabb, &intersect);
      if (tmin) {
        ring[ringBack] = voxels[voxBig.BBL];
        ringInd[ringBack] = voxBig.BBL;
        ringAabb0[ringBack] = testaabb[0];
        ringAabb1[ringBack] = testaabb[1];
        #ifdef HALP
        printf("BBL is interesting; pushing voxel %d\n", ringInd[ringBack]);
        #endif
        ringBack = (ringBack + 1) % RING_LENGTH;
        c++;
      }

      // BBR
      testaabb[0] = (float3)(midx, midy, midz);
      testaabb[1] = (float3)(testaabb[0].x + vs, testaabb[0].y + vs, testaabb[0].z + vs);
      tmin = ray_aabb_intersection_distance(&ray, testaabb, &intersect);
      if (tmin) {
        ring[ringBack] = voxels[voxBig.BBR];
        ringInd[ringBack] = voxBig.BBR;
        ringAabb0[ringBack] = testaabb[0];
        ringAabb1[ringBack] = testaabb[1];
        #ifdef HALP
        printf("BBR is interesting; pushing voxel %d\n", ringInd[ringBack]);
        #endif
        ringBack = (ringBack + 1) % RING_LENGTH;
        c++;
      }
      
    } else if (r == 0) {
      // Simple! :)
      //printf("%s", "BRIGHT\n");

      // get distance
      //if (aabb[0].z < bestDistance) {
      if (intersect < bestDistance) {
#if HALP
        printf("%s", "best candidate now this voxel\n");
#endif
        bestAabb0 = aabb[0];
        bestAabb1 = aabb[1];
        bestDistance = intersect;
        bestVoxel = voxBig;
      }

    }

    // no result?
    // pull from ring buffer
    if (ringFront != ringBack) {
      voxBig = ring[ringFront];
      aabb[0] = ringAabb0[ringFront];
      aabb[1] = ringAabb1[ringFront];
#if HALP
      printf("round %d will be voxel %d xyz %f %f %f to %f %f %f (Ring Len %d)\n", i + 1, ringInd[ringFront], aabb[0].x, aabb[0].y, aabb[0].z, aabb[1].x, aabb[1].y, aabb[1].z, (ringBack - ringFront > 0 ? ringBack - ringFront : ringBack + RING_LENGTH - ringFront));
#endif

      ringFront++;
      if (ringFront >= RING_LENGTH) {
        ringFront = 0;
      }
    } else {
      // out of candidates? then we're done.
      //printf("%s", "out of candidates\n");
      
      if (bestDistance < 100000.0f) {
#if HALP
        printf("using best candidate with dist %f\n", bestDistance);
#endif

#if HALP
        printf("%s", "good enough, i'ma light it\n");
#endif

        // Let's light this bad boy!
        float3 lightPosition = (float3)(100.0f, -100.0f, -200.0f);
        float3 diffuseColor = (float3)(1.0f, 1.0f, 1.0f);
        float diffusePower = 100000.0f;
        // blah blah todo specular

        // from voxel
        /*float3 voxPosition = bestAabb0; // TODO: MAKE BETTER
        voxPosition.x = x;
        voxPosition.y = y;*/
        float3 voxPosition = ray.origin + ray.direction * bestDistance;
        //float3 viewDir = (float3)(0.0f, 0.0f, 1.0f);
        float3 viewDir = ray.direction;
        // sphere hack. should be stored on voxel or summat!
        float3 normal = normalize(voxPosition - (float3)(0.0f, 0.0f, 128.0f));

        float3 lightDir = lightPosition - voxPosition;
        float distance = length(lightDir);
        lightDir = lightDir / distance;
        distance = distance * distance;

        float nDotL = dot(normal, lightDir);
        float intensity = clamp(nDotL, 0.0f, 1.0f);

        float r = 0.5f + intensity * diffuseColor.x * diffusePower / distance;
#if HALP
        printf("result %f (SOLID)\n", r);
#endif
        return r;

      } else {
#if HALP
        printf("%s", "result 0.0 (out of candidates)\n");
#endif
        return 0.0f;
      }
    }

  } // end for

  // uhhh, we ran out of guesses before we got to the answer.
  // let's....... guess.
#if HALP
  printf("%s", "DUNNO\n");
#endif
  return 0.25f;

} // end getVoxelIntensityForPixel

#ifdef HALP
kernel void helloVoxel(global Voxel* voxels, uint svox, write_only image2d_t outimg, float vfx, float vfy, size_t x, size_t y)
#else
kernel void helloVoxel(global Voxel* voxels, uint svox, write_only image2d_t outimg, float vfx, float vfy)
#endif
{
  float f;
  uint intensity;
  uint r;
  uint g;
  uint b;
  uint4 outc;

#ifndef HALP
  size_t x = get_global_id(0);
  size_t y = get_global_id(1);
  //for (int y = 0; y < HEIGHT; y++) {
  //for (int x = 0; x < HEIGHT; x++) {
  //printf("x %d y %d\n", x, y);
#endif

  f = clamp(getVoxelIntensityForPixel(voxels, svox, (float)x - (float)WIDTH_DIV_2, (float)y - (float)HEIGHT_DIV_2, vfx, vfy), 0.0f, 1.0f);

  // float -> uchar
  intensity = (uint) (255.0f * f);
  r = intensity; g = intensity; b = intensity;

  // do we need a BGRA or RGBA image?
#ifdef BGRA
  outc = (uint4) (b, g, r, 255);
#else
  outc = (uint4) (r, g, b, 255);
#endif
#if HALP
  printf("image2d_t outimg x y %d %d R G B %d %d %d (fint %f)\n", x, y, r, g, b, f);
#endif

  write_imageui(outimg, (int2)((int)x, (int)y), outc);
#ifndef HALP
//  }
  //}
#endif
}

