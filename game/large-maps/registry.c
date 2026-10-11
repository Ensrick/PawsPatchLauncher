/* LOCAL EXPERIMENT. Keep the primary 16-bit actor table in its original
 * location. Three additional banks hold generic map objects. No OS calls,
 * RNG, thread scheduling, object ownership or simulation decisions here. */
typedef unsigned int U;
typedef unsigned short W;
#define P(a,o) (*(U*)((U)(a)+(o)))
#define G(r,i) (*(W*)((r)+4+2*(i)))
#define EXPORT __attribute__((dllexport))
#define EXTRA 196608u
#define CAP 262144u
typedef U (*Call2)(U,U,U,U);
typedef struct { U image,call2,mode,count,next,high,error,reserved; W gen[EXTRA]; U slots[EXTRA]; U warning_text; U minimap_pixels[64*64]; } State;
#include "large_map_warning_text.h"
/* UI only: applied mob frequency and game-text language are fixed at launch.
 * Re-evaluate the current map every time Start is pressed, including after
 * cancellation. Saved games and replays never pass through this warning. */
EXPORT U large_map_warning(State*d) {
 U session=P(d->image,0x5f3fe4),settings,frequency=d->reserved&255,language=(d->reserved>>8)&255;
 d->warning_text=0;
 if((frequency!=2&&frequency!=4)||!session||P(session,0x6c)!=1||P(session,0x64)!=0)return 0;
 settings=P(session,0x80);
 if(!settings||!(*(float*)(settings+0x3c)>=1536.0f&&*(float*)(settings+0x40)>=1536.0f))return 0;
 d->warning_text=(U)warning_translations[language<6?language:0];
 return d->warning_text;
}
/* RMinimap rasterizes textured markers at a fixed pixel size. Resample only
 * their local pixel source; native positioning, fog, tint, selection outline
 * and clipping are unchanged. Tiny company dots use a separate native path.
 * The scratch pixels are consumed synchronously before the next marker. */
static int minimap_int(float f){int i;__asm__ volatile(".byte 0xf3,0x0f,0x2c,0x00":"=a"(i):"a"(&f));return i;}
EXPORT U minimap_icon(State*d,U frame,U width) {
 U world=P(d->image,0x5f3fb8),map,height=P(frame,0x90),stride=P(frame,0x80),source=P(frame,0xc8);
 U x,y,w,h,sx,sy;float side,other;
 if(!world||!source||!width||!height||width>64||height>64||stride<width)return width;
 map=P(world,0x30);if(!map)return width;
 side=*(float*)(map+0xc);other=*(float*)(map+0x10);
 if(!(side>0&&other>0&&side<=65536&&other<=65536))return width;
 if(other>side)side=other;
 if(side<=1024)return width;
 w=minimap_int(width*1024.0f/side+0.5f);h=minimap_int(height*1024.0f/side+0.5f);
 /* Native placement subtracts floor(size/2). An even reduced size moves
  * its visual center half a texel left/up and loses odd-source center art
  * (the stock 7x7 city marker became 4x4, skipping its black center dot).
  * Retain a center texel; prefer the next odd size for tiny-icon legibility. */
 if(!(w&1))w++;if(!(h&1))h++;
 if(w<3)w=3;if(h<3)h=3;if(w>width)w=width;if(h>height)h=height;
 if(w==width&&h==height)return width;
 for(y=0;y<h;y++){
  sy=((2*y+1)*height)/(2*h);
  for(x=0;x<w;x++){sx=((2*x+1)*width)/(2*w);d->minimap_pixels[y*w+x]=P(source,4*(sy*stride+sx));}
 }
 P(frame,0x90)=h;P(frame,0x80)=w;P(frame,0xc8)=(U)d->minimap_pixels;
 return w;
}
EXPORT U registry_state_size=sizeof(State);
int __attribute__((stdcall)) _dllstart(void*a,U b,void*c){return 1;}
static U flat(State*d,U id){return (id&65535)|((id>>14)&0x30000);}
static U generation(State*d,U id){return d->mode==16?id>>16:(id>>16)&16383;}
static U make_id(State*d,U i,U g){return (i&65535)|(g<<16)|((i&0x30000)<<14);}
static U *slot(State*d,U r,U i){return i<65536?(U*)(r+0x20004+4*i):&d->slots[i-65536];}
static W *gen(State*d,U r,U i){return i<65536?(W*)(r+4+2*i):&d->gen[i-65536];}
static U index(State*d,U id){return d->mode==16?id&65535:flat(d,id);}
static void bit(U r,U i,U on){U *b=(U*)P(r,0x60010);if(on)b[i/32]|=1u<<(i%32);else b[i/32]&=~(1u<<(i%32));}
static void error(State*d){d->error++;((void(*)(const char*))(d->image+0x1c5782))("Invalid local large-map object registry or unsupported save");}
EXPORT U lookup(State*d,U r,U id){U i=index(d,id);return id&&*gen(d,r,i)==generation(d,id)?*slot(d,r,i):0;}
EXPORT U generation_matches(State*d,U r,U id){return *gen(d,r,index(d,id))==generation(d,id);}
EXPORT U by_index(State*d,U r,U i){return i<CAP?*slot(d,r,i):0;}
EXPORT U id_index(State*d,U r,U id){return index(d,id);}
EXPORT U unchecked_lookup(State*d,U r,U id){return by_index(d,r,index(d,id));}
EXPORT U clear(State*d,U r){U i;d->mode=18;d->count=0;d->next=0;d->high=0;d->error=0;
 for(i=0;i<EXTRA;i++){d->gen[i]=0;d->slots[i]=0;}
 for(i=0;i<65536;i++){G(r,i)=0;P(r,0x20004+4*i)=0;}
 for(i=0;i<2048;i++)P(P(r,0x60010),4*i)=0;
 P(r,0x60004)=0;P(r,0x60008)=1;P(r,0x6000c)=0;return 0;
}
EXPORT U insert(State*d,U r,U obj,U id){U i=index(d,id);if(!i||*slot(d,r,i)){error(d);return 0;}
 *slot(d,r,i)=obj;P(obj,0x14)=id;P(r,0x60004)++;
 if(i<65536){bit(r,i,1);if(i>P(r,0x6000c))P(r,0x6000c)=i;}
 else{d->count++;if(i-65536>d->high)d->high=i-65536;}
 return 1;
}
EXPORT U add(State*d,U r,U out,U obj){U i,n,g,ext=d->mode==18&&P(obj,0)==d->image+0x4e8f80;
 if(ext){
  if(d->count>=EXTRA){error(d);P(out,0)=0;return out;}
  i=d->next;for(n=0;n<EXTRA&&d->slots[i];n++)i=(i+1)%EXTRA;
  g=d->gen[i];d->next=(i+1)%EXTRA;i+=65536;
 }else{
  if(P(r,0x60004)-d->count>=65535){error(d);P(out,0)=0;return out;}
  i=P(r,0x60008);for(n=0;n<65535&&P(r,0x20004+4*i);n++)i=i==65535?1:i+1;
  g=G(r,i);P(r,0x60008)=i==65535?1:i+1;
 }
 g=make_id(d,i,g);insert(d,r,obj,g);P(out,0)=g;return out;
}
EXPORT U remove_object(State*d,U r,U obj){U id=P(obj,0x14),i=index(d,id),h;if(!id||!*slot(d,r,i))return 0;
 *slot(d,r,i)=0;P(r,0x60004)--;*gen(d,r,i)=(*gen(d,r,i)+1)&(d->mode==16?65535:16383);
 if(i<65536){bit(r,i,0);h=P(r,0x6000c);if(i==h){while(h&&P(r,0x20004+4*h)==0)h--;P(r,0x6000c)=h;}}
 else{d->count--;i-=65536;if(i==d->high){while(d->high&&!d->slots[d->high])d->high--;}}
 return 0;
}
EXPORT U restore(State*d,U r,U obj,U id){U i=index(d,id);if(!id||!i||*slot(d,r,i)||*gen(d,r,i)!=generation(d,id)){error(d);return 0;}return insert(d,r,obj,id);}
EXPORT U set_generation(State*d,U r,U id){*gen(d,r,index(d,id))=(W)generation(d,id);return 0;}
EXPORT U swap_objects(State*d,U r,U old,U obj,U provided){U id=old?P(old,0x14):provided,prior=P(obj,0x14);
 *slot(d,r,index(d,id))=obj;P(obj,0x14)=id;*slot(d,r,index(d,prior))=old;if(old)P(old,0x14)=prior;return 0;}
EXPORT U next_object(State*d,U r,U cursor){U id=P(cursor,0),i=id?index(d,id)+1:1,obj;
 if(i<65536){for(;i<=P(r,0x6000c);i++)if((obj=P(r,0x20004+4*i))!=0){P(cursor,0)=P(obj,0x14);return obj;}i=65536;}
 if(d->mode==18)for(;i<=65536+d->high;i++)if((obj=d->slots[i-65536])!=0){P(cursor,0)=P(obj,0x14);return obj;}
 P(cursor,0)=0;return 0;
}
static void io(State*d,U s,U ptr,U bytes,U writing){((Call2)d->call2)(P(P(s,0),writing?4:0),s,ptr,bytes);}
static void scope(State*d,U s){U scratch;((Call2)d->call2)(d->image+0x303310,(U)&scratch,s,0);}
EXPORT U save(State*d,U r,U ctx){U s=ctx+0x3c,cap=d->mode==16?65536:CAP;scope(d,s);
 io(d,s,r+0x60008,4,1);io(d,s,r+0x6000c,4,1);io(d,s,(U)&cap,4,1);
 if(d->mode==16){io(d,s,r+4,P(r,0x6000c)*2,1);return 0;}
 io(d,s,(U)&d->next,4,1);io(d,s,(U)&d->high,4,1);io(d,s,r+4,0x20000,1);io(d,s,(U)d->gen,EXTRA*2,1);return 0;
}
EXPORT U load(State*d,U r,U ctx){U s=ctx+0x3c,cap=0,i;scope(d,s);
 io(d,s,r+0x60008,4,0);io(d,s,r+0x6000c,4,0);io(d,s,(U)&cap,4,0);
 if(P(r,0x60008)<1||P(r,0x60008)>65535||P(r,0x6000c)>65535){error(d);return 0;}
 if(cap==65536){d->mode=16;io(d,s,r+4,P(r,0x6000c)*2,0);return 0;}
 if(cap!=CAP){error(d);return 0;}d->mode=18;
 io(d,s,(U)&d->next,4,0);io(d,s,(U)&d->high,4,0);
 if(d->next>=EXTRA||d->high>=EXTRA){error(d);return 0;}
 io(d,s,r+4,0x20000,0);io(d,s,(U)d->gen,EXTRA*2,0);
 for(i=0;i<65536;i++)if(G(r,i)>16383){error(d);return 0;}
 for(i=0;i<EXTRA;i++)if(d->gen[i]>16383){error(d);return 0;}
 return 0;
}
/* The stock pointer codec writes only 16 index bits. Widen only these
 * object-reference call sites, never the shared uint16 serializer. */
EXPORT U write_reference(State*d,U r,U stream,U obj){U i=obj?index(d,P(obj,0x14)):0;
 ((void(*)(U,U,U))(d->image+0x518fe))(stream,(U)&i,d->mode);return 0;}
EXPORT U read_reference(State*d,U r,U stream){U i=0;((void(*)(U,U,U))(d->image+0x517cc))(stream,(U)&i,d->mode);return by_index(d,r,i);}
