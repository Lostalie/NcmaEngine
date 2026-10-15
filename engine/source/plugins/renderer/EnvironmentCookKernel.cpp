#include "EnvironmentCookKernel.h"
#include <Eigen/Core>
#include <Eigen/Geometry>
#include <cmath>
#include <numbers>
#include <algorithm>
#include <stdexcept>
namespace NcmaEngine::Rendering::EnvironmentCook {
namespace {
using Vec=Eigen::Vector3d;
constexpr double Pi=std::numbers::pi;
bool Power(uint32_t n,uint32_t low,uint32_t high){return n>=low&&n<=high&&(n&(n-1))==0;}
double Radical(uint32_t i){double result=0,weight=.5;while(i){result+=(i&1)*weight;weight*=.5;i>>=1;}return result;}
Vec Face(uint32_t f,uint32_t x,uint32_t y,uint32_t size){
 const double u=2*(x+.5)/size-1,v=2*(y+.5)/size-1;
 switch(f){case 0:return Vec(1,-v,-u).normalized();case 1:return Vec(-1,-v,u).normalized();
 case 2:return Vec(u,1,v).normalized();case 3:return Vec(u,-1,-v).normalized();
 case 4:return Vec(u,-v,1).normalized();default:return Vec(-u,-v,-1).normalized();}
}
// A texel's basis does not depend on its integration sample. Keep the original
// arithmetic/order, but compute the two cross products and normalization once.
struct Basis {
 Vec n,t,b;
 explicit Basis(const Vec& normal):n(normal){
  const Vec up=std::abs(n.z())<.999?Vec(0,0,1):Vec(1,0,0);
  t=up.cross(n).normalized();b=n.cross(t);
 }
 Vec Apply(const Vec& local) const {
  Vec result;auto* out=result.data();const auto* tangent=t.data();const auto* bitangent=b.data();const auto* normal=n.data();const auto* sample=local.data();
  for(int c=0;c<3;c++)out[c]=(tangent[c]*sample[0]+bitangent[c]*sample[1])+normal[c]*sample[2];
  return result;
 }
};
Vec Sample(const std::vector<float>& source,uint32_t w,uint32_t h,const Vec& d){
 const double px=(std::atan2(d.z(),d.x())/(2*Pi)+.5)*w-.5,py=std::acos(std::clamp(d.y(),-1.,1.))/Pi*h-.5;
 const int x0=static_cast<int>(std::floor(px)),y0=static_cast<int>(std::floor(py));const double fx=px-x0,fy=py-y0;
 // Coordinates are shared by RGB and by the two rows/columns. Resolve each
 // once, then evaluate the same bilinear expression per channel. This avoids
 // constructing four Eigen expression trees for every integration sample.
 auto wrap=[&](int x){return static_cast<size_t>((x%static_cast<int>(w)+static_cast<int>(w))%static_cast<int>(w))*4;};
 const size_t left=wrap(x0),right=wrap(x0+1);
 const size_t top=static_cast<size_t>(std::clamp(y0,0,static_cast<int>(h)-1))*w*4;
 const size_t bottom=static_cast<size_t>(std::clamp(y0+1,0,static_cast<int>(h)-1))*w*4;
 Vec result;auto* out=result.data();
 for(size_t c=0;c<3;c++)out[c]=
  ((1-fx)*source[top+left+c]+fx*source[top+right+c])*(1-fy)+
  ((1-fx)*source[bottom+left+c]+fx*source[bottom+right+c])*fy;
 return result;
}
Vec GgxLocal(double u,double v,double rough){
 const double a=rough*rough,c=std::sqrt((1-v)/(1+(a*a-1)*v)),s=std::sqrt(std::max(0.,1-c*c)),phi=2*Pi*u;
 return Vec(s*std::cos(phi),s*std::sin(phi),c);
}
double Geometry(double cosine,double rough){const double k=rough*rough*.5;return cosine/(cosine*(1-k)+k);}
}
bool Describe(const NcmaEnvironmentCookV1& d,Layout& out) noexcept {
 if(d.struct_size!=48||d.version!=1||d.reserved||!Power(d.height,2,256)||d.width!=d.height*2||
 !Power(d.cube_size,2,64)||!Power(d.irradiance_size,2,16)||!Power(d.lut_size,2,64)||!Power(d.samples,64,2048)||
 d.input_floats!=d.width*d.height*4)return false;
 uint64_t spec=0;uint32_t levels=0;for(uint32_t size=d.cube_size;size;size/=2){spec+=6ull*size*size;levels++;}
 const uint64_t diffuse=6ull*d.irradiance_size*d.irradiance_size,lut=static_cast<uint64_t>(d.lut_size)*d.lut_size;
 const uint64_t floats=(spec+diffuse)*4+lut*2,work=(spec+diffuse+lut)*d.samples;
 if(work>64ull*1024*1024||floats*4>4ull*1024*1024)return false;
 out={static_cast<uint32_t>(floats),levels,work};return true;
}
bool ValidInput(const std::vector<float>& values) noexcept {
 for(size_t i=0;i<values.size();i++){const float x=values[i];if(!std::isfinite(x)||x<0||x>65504||(x==0&&std::signbit(x))||(i%4==3&&x!=1))return false;}return true;
}
std::vector<float> Run(const NcmaEnvironmentCookV1& d,const std::vector<float>& source,const Layout& layout){
 std::vector<float> result;result.reserve(layout.floats);
 auto color=[&](const Vec& v){for(int c=0;c<3;c++){const double x=v[c];if(!std::isfinite(x)||x<0||x>65504*Pi+1)throw std::runtime_error("Environment numerical range.");
 result.push_back(static_cast<float>(x));}result.push_back(1);};
 // One bounded scratch vector (at most 2048 * sizeof(Vec)) is reused for
 // diffuse, each specular mip and each BRDF row. No persistent cache or GPU
 // resource; sample sequence, precision and accumulation order stay unchanged.
 std::vector<Vec> samples;samples.reserve(d.samples);
 for(uint32_t i=0;i<d.samples;i++){const double u=(i+.5)/d.samples,v=Radical(i),r=std::sqrt(u),phi=2*Pi*v;
 samples.emplace_back(r*std::cos(phi),r*std::sin(phi),std::sqrt(1-u));}
 for(uint32_t f=0;f<6;f++)for(uint32_t y=0;y<d.irradiance_size;y++)for(uint32_t x=0;x<d.irradiance_size;x++){
 const Basis basis(Face(f,x,y,d.irradiance_size));Vec sum=Vec::Zero();
 for(uint32_t i=0;i<d.samples;i++){const Vec dir=basis.Apply(samples[i]),value=Sample(source,d.width,d.height,dir);
 for(int c=0;c<3;c++)sum.data()[c]+=value.data()[c];}
 color(sum*(Pi/d.samples));}
 uint32_t level=0;
 for(uint32_t size=d.cube_size;size;size/=2,level++){
 const double rough=static_cast<double>(level)/(layout.levels-1);
 if(level>0)for(uint32_t i=0;i<d.samples;i++)samples[i]=GgxLocal((i+.5)/d.samples,Radical(i),rough);
 for(uint32_t f=0;f<6;f++)for(uint32_t y=0;y<size;y++)for(uint32_t x=0;x<size;x++){
 const Vec n=Face(f,x,y,size);if(level==0){color(Sample(source,d.width,d.height,n));continue;}
 const Basis basis(n);Vec sum=Vec::Zero();double weight=0;
 for(uint32_t i=0;i<d.samples;i++){const Vec h=basis.Apply(samples[i]);const Vec l=2*n.dot(h)*h-n;const double nl=std::max(0.,n.dot(l));
 if(nl>0){const Vec value=Sample(source,d.width,d.height,l);for(int c=0;c<3;c++)sum.data()[c]+=value.data()[c]*nl;weight+=nl;}}
 if(weight<=0)throw std::runtime_error("Environment zero weight.");color(sum/weight);}}
 const Basis lutBasis(Vec(0,0,1));
 for(uint32_t y=0;y<d.lut_size;y++){
 const double rough=std::max(.045,static_cast<double>(y)/(d.lut_size-1));
 for(uint32_t i=0;i<d.samples;i++)samples[i]=lutBasis.Apply(GgxLocal((i+.5)/d.samples,Radical(i),rough));
 for(uint32_t x=0;x<d.lut_size;x++){
 const double nv=std::max(.0001,static_cast<double>(x)/(d.lut_size-1));
 const double vx=std::sqrt(std::max(0.,1-nv*nv)),geometryView=Geometry(nv,rough);double a=0,b=0;
 for(uint32_t i=0;i<d.samples;i++){const auto* h=samples[i].data();const double vh=std::max(0.,vx*h[0]+nv*h[2]);
 // The LUT only consumes reflected z; do not build unused x/y expressions.
 const double nl=std::max(0.,2*vh*h[2]-nv),nh=std::max(.000001,h[2]);if(nl<=0)continue;
 const double visible=geometryView*Geometry(nl,rough)*vh/(nh*nv),f=std::pow(1-vh,5);a+=(1-f)*visible;b+=f*visible;}
 a/=d.samples;b/=d.samples;if(!std::isfinite(a)||!std::isfinite(b)||a<0||b<0||a>2||b>2)throw std::runtime_error("Environment LUT range.");
 result.push_back(static_cast<float>(a));result.push_back(static_cast<float>(b));}}
 if(result.size()!=layout.floats)throw std::runtime_error("Environment layout mismatch.");return result;
}
}
