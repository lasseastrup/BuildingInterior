#ifndef URP_FOG_STUB
#define URP_FOG_STUB
#define FOG_LINEAR_KEYWORD_DECLARED
static const bool FOG_LINEAR = true, FOG_EXP = false, FOG_EXP2 = false;
float ComputeFogFactorZ0ToFar(float z) { return z * 0.01; }
float ComputeFogIntensity(float f) { return saturate(f); }
#endif
