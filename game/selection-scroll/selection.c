/* Selection remains native and dynamic. Cards outside the visible page stay
 * attached but hidden: their native ticks retain every destination flag.
 * This module never truncates the command selection or writes simulation state. */
typedef unsigned int U;
#define P(a,o) (*(U*)((U)(a)+(o)))
#define F(a,o) (*(float*)((U)(a)+(o)))
#define FAST __attribute__((fastcall))
#define EXPORT __attribute__((dllexport))
typedef U (FAST *M0)(U,U);
typedef U (FAST *M1)(U,U,U);
typedef U (FAST *M2)(U,U,U,U);
typedef int (__attribute__((stdcall)) *POINTFN)(U,void*);
typedef U (*FACTORY)(U,U);
typedef void (FAST *MOVE)(U,U,float,float);
int __attribute__((stdcall)) _dllstart(void*a,U b,void*c){return 1;}
typedef struct {U image,group,world,selection,head,count,offset,busy;int wheel;U builds,scrolls;
 U extraHead,extraTail,pageSize,serial;} Data;
static U m0(Data*d,U r,U s){return ((M0)(d->image+r))(s,0);}
static U m1(Data*d,U r,U s,U a){return ((M1)(d->image+r))(s,0,a);}
static U v0(U s,U r){return ((M0)P(P(s,0),r))(s,0);}
static U v1(U s,U r,U a){return ((M1)P(P(s,0),r))(s,0,a);}
static U count(U head){U n=0;while(head){n++;head=P(head,4);}return n;}
static int integer(float f){int n;__asm__ volatile(".byte 0xf3,0x0f,0x2c,0x00":"=a"(n):"a"(&f));return n;}
static void release_extras(Data*d){
 U node,card;
 for(node=d->extraHead;node;node=P(node,4)){
  card=P(node,0);
  ((M2)(d->image+0x2b74f9))(d->group,0,card,0);
  m1(d,0xdafe9,d->group,node);
 }
 m0(d,0x20d4b,(U)&d->extraHead);
}
static U take_card(Data*d,U actor){
 U g=d->group,pool=0x74,factory=0xde843,card;
 if(v0(actor,0xb4)){
  if(P(actor,0x7c)){pool=0x7c;factory=0xde633;}
  else if(v0(actor,0x15c)){pool=0x84;factory=0xde967;}
 }
 if(P(g,pool))return m1(d,0xdaf89,g,actor);
 /* New cards use the same native constructor/configuration and ownership as
  * the stock pool. They return to that pool when the page is released. */
 card=((FACTORY)(d->image+factory))(0x1000+d->serial++,0);
 if(!card)return 0;
 m1(d,0x2b6c58,card,g);
 v1(card,0x1c,0);
 return card;
}
static void populate_extras(Data*d,U head){
 U node=head,i,actor,card,g=d->group;
 for(i=0;i<P(g,0xb8)&&node;i++)node=P(node,4);
 for(;node;node=P(node,4)){
  actor=m0(d,0x2e0fa6,P(node,0));if(!actor)continue;
  card=take_card(d,actor);if(!card)break;
  m1(d,0x20ad2,(U)&d->extraHead,(U)&card);
  v1(g,0xd0,v1(card,0xec,actor));
 }
}
static void layout_cards(Data*d){
 U g=d->group,n=P(g,0xb8),i,card,node,view=P(g,0x10),fv=P(P(g,0xa8),0x10),cv,visible;
 float scale,pitch,width,available,right;
 int capacity;
 d->pageSize=6;
 if(!n)return;
 card=P(g,0x90);cv=P(card,0x10);width=F(cv,0x28)-F(cv,0x20);
 /* Horizontal pixels and native logical positions must share one scale. */
 scale=(float)P(d->image,0x5f9228)/F(P(P(d->image,0x5f94d0),0x88),0);
 if(width<=0||scale<=0)return;
 pitch=width/scale+5.0f;right=F(view,0x28);
 if(F(fv,0x20)>F(view,0x20)&&F(fv,0x20)<right)right=F(fv,0x20);
 available=(right-F(view,0x20))/scale;
 capacity=integer((available+5.0f)/pitch);
 d->pageSize=capacity<6?6:capacity;
 node=d->extraHead;
 for(i=0;i<n||node;i++){
  if(i<n)card=P(g,0x90+i*4);else{card=P(node,0);node=P(node,4);}
  visible=i>=d->offset&&i<d->offset+d->pageSize;
  m1(d,0x2b7271,card,visible);
  if(visible)((MOVE)(d->image+0x2b701c))(card,0,pitch*(i-d->offset),F(g,0x8c));
 }
}
static void formations(Data*d,U head){
 U g=d->group,f=P(g,0xa8),manager=P(d->image,0x5f3fc0),local,node,actor;
 /* Native reset releases its linked actor-ID list and three formation entries. */
 if(P(f,8)==g)((M2)(d->image+0x2b74f9))(g,0,f,0);
 v1(g,0xd0,f);
 local=m0(d,0x2be624,manager);
 for(node=head;node;node=P(node,4)){
  actor=m0(d,0x2e0fa6,P(node,0));
  if(actor&&v0(actor,0xb4)&&P(actor,0x7c)&&v1(actor,0x134,local)&&v1(actor,0x34,0x21))m1(d,0xdc495,f,actor);
 }
 if(m0(d,0xdc623,f)<=1)((M2)(d->image+0x2b74f9))(g,0,f,0);
}
static void render(Data*d,U head){
 d->busy=1;
 release_extras(d);
 m1(d,0xdacca,d->group,(U)&head);
 populate_extras(d,head);
 layout_cards(d);
 /* Stock build only saw the page. Formation commands must see everybody. */
 if(d->count>6)formations(d,head);
 d->busy=0;d->builds++;
}
EXPORT void build(U image,Data*d,U group,U list){
 U head=P(list,0),world=P(image,0x5f3fb8),ui=P(image,0x5f3fbc),n=count(head),sel=ui?P(ui,0x13c):0;
 d->image=image;
 /* A selection change starts at the first card. Scrolling itself calls stock
  * directly, so it cannot alter selection or trigger this reset. */
 d->group=group;d->world=world;d->selection=sel;d->head=head;d->count=n;d->offset=0;d->wheel=0;
 render(d,head);
}
EXPORT void reset(U image,Data*d,U group){
 d->image=image;
 if(d->group==group)release_extras(d);
 if(!d->busy&&d->group==group){d->group=0;d->head=0;d->count=0;d->offset=0;d->wheel=0;}
}
static int active(Data*d){
 U ui=P(d->image,0x5f3fbc),g=d->group,p,sel;
 if(!g||!ui||P(d->image,0x5f9218)!=2||P(d->image,0x5f3fb8)!=d->world)return 0;
 sel=P(ui,0x13c);
 if(!sel||sel!=d->selection||P(sel,8)!=d->head)return 0;
 /* Parent visibility is essential when a modal or another selection panel
  * temporarily replaces GroupDisplay. Detached pooled widgets are excluded. */
 for(p=g;p;p=P(p,8))if(!(P(p,0x24)&1))return 0;
 return P(g,8)!=0&&P(g,0xb8)>0;
}
EXPORT U wheel(U image,Data*d,U hwnd,U wp,U lp){
 U g,view,form,n;int origin[2]={0,0},x,y,steps,offset;float right;
 d->image=image;
 if(d->busy||!active(d)||d->count<=d->pageSize)return 0;
 g=d->group;view=P(g,0x10);form=P(P(g,0xa8),0x10);
 if(!view||!form)return 0;
 if(!((POINTFN)P(image,0x44d2b4))(hwnd,origin))return 0;
 x=(short)(lp&65535)-origin[0];y=(short)(lp>>16)-origin[1];
 right=F(view,0x28);if(F(form,0x20)>F(view,0x20)&&F(form,0x20)<right)right=F(form,0x20);
 if(x<F(view,0x20)||x>=right||y<F(view,0x24)||y>=F(view,0x2c))return 0;
 n=count(P(d->selection,8));if(n!=d->count)return 0;
 d->wheel+=(short)(wp>>16);steps=d->wheel/120;d->wheel-=steps*120;
 if(!steps)return 1;
 offset=(int)d->offset-steps;if(offset<0)offset=0;if(offset>(int)n-(int)d->pageSize)offset=n-d->pageSize;
 if((U)offset!=d->offset){d->offset=offset;layout_cards(d);d->scrolls++;}
 return 1;
}
