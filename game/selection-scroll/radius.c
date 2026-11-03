/* The native group destination mask has a six-company radius (normally 15).
 * Extend only that mask, not player sight, pathfinding or individual orders.
 * Called at group-only mask updates; no state/cache writes. */
typedef unsigned int U;
#define P(a,o) (*(U*)((U)(a)+(o)))
#define F(a,o) (*(float*)((U)(a)+(o)))
int __attribute__((stdcall)) _dllstart(void*a,U b,void*c){return 1;}
static float rounded(float x){volatile float y=x;return y;}
__attribute__((dllexport)) U radiusSquared(U image,U component){
 U g,form,t,n; int k,i,cols=0; float x,y,mx=0,my=0,best=0,step=0;
 union {float f;U u;} result;
 if(!component)return 0;
 g=P(component,4);
 if(!g||P(g,0)!=image+0x4e7d60)return 0;
 n=P(g,0x194);form=P(g,0x1b8);
 if(n<=6||n>256||!form||P(form,28)!=6||!(t=P(form,24)))return 0;
 for(k=0;k<3;k++){
  for(i=0;i<6;i++){
   if(k==1){x=(i%2)?-5.75f:5.75f;y=8.0f-(i/2)*8.0f;}
   else{x=(i%3==0)?0.0f:(i%3==1?-(k==0?10.0f:4.0f):(k==0?10.0f:4.0f));y=(k==0?4.0f:8.0f)-(i/3)*(k==0?8.0f:16.0f);}
   if(F(t,i*8)!=x||F(t,i*8+4)!=y)break;
  }
  if(i==6){cols=k==1?2:3;break;}
 }
 if(!cols)return 0;
 step=F(t,cols*8+4)-F(t,4);
 for(i=0;i<(int)n;i++){
  mx=rounded(mx+F(t,(i%cols)*8));
  my=rounded(my+rounded(F(t,(i%cols)*8+4)+rounded((i/cols)*step)));
 }
 mx=rounded(mx*rounded(1.0f/n));my=rounded(my*rounded(1.0f/n));
 for(i=0;i<(int)n;i++){
  x=rounded(F(t,(i%cols)*8)-mx);
  y=rounded(rounded(F(t,(i%cols)*8+4)+rounded((i/cols)*step))-my);
  x=rounded(rounded(x*x)+rounded(y*y));if(x>best)best=x;
 }
 result.f=best;return result.u;
}
