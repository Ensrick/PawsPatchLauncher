/* Included by policy.c. Only the path solver's locked, synchronous query owns
 * this context. Cell hooks check the owner thread; human/formation-member paths
 * never acquire an active context. No persistent AI or actor state is written. */
#define FAST __attribute__((fastcall))
typedef U (FAST *RouteM1)(U,U,U);
typedef float (FAST *RouteF0)(U,U);
static U route_thread(void){U t;__asm__ volatile("movl %%fs:0x24,%0":"=r"(t));return t;}
/* cvttss2si eax,[eax]; the bundled TCC assembler predates SSE mnemonics. */
static int route_int(float f){int i;__asm__ volatile(".byte 0xf3,0x0f,0x2c,0x00":"=a"(i):"a"(&f));return i;}
static float maximum(float a,float b){return a>b?a:b;}
static float minimum(float a,float b){return a<b?a:b;}
static float dist2(float x,float y,float a,float b){x-=a;y-=b;return x*x+y*y;}
static int route_active(U image,Route*r){return r->count&&r->depth==1&&r->thread==route_thread()&&r->image==image&&r->world==P(image,0x5f3fb8);}
static U route_cell(U image,float x,float y){
 U world=P(image,0x5f3fb8),grid=world?P(world,0x30):0;int ix,iy;U shift;
 if(!grid||!finite(x)||!finite(y)||x<0||y<0||x>=F(grid,0xc)||y>=F(grid,0x10))return 0;
 shift=P(grid,4);if(shift>8)return 0;ix=route_int(x)>>shift;iy=route_int(y)>>shift;
 return P(grid,0x28)+(iy*P(grid,0x1c)+ix)*0x30;
}
static U economy_center(U def,U layout,U depth);
static int route_builder(U image,U actor){
 U b=P(actor,0xa8),org=P(actor,0x7c),i,n,list;
 if(b&&P(b,0)==image+0x4df830&&P(b,4)==actor)return 1;
 if(!org)return 0;
 /* A newly hired company initially contains only its captain. Its actual
  * organization layout already identifies the workers which will arrive.
  * Purpose is distinct from construction readiness/capability below. */
 if(P(actor,4)&&(P(P(actor,4),0x174)&128)&&economy_center(P(actor,4),org+0x10,0))return 1;
 n=P(org,0x2c);list=P(org,0x28);if(n>64||!list)return 0;
 for(i=0;i<n;i++){U unit=P(list,4*i);if(!unit||!live_actor(image,unit))continue;b=P(unit,0xa8);if(b&&P(b,0)==image+0x4df830&&P(b,4)==unit)return 1;}
 return 0;
}
static int ids_equal(U def,const char* text);
static int route_center_capable(U image,U actor){
 U b=P(actor,0xa8),def=P(actor,4),node,i=0;
 if(!b||P(b,0)!=image+0x4df830||P(b,4)!=actor||!def)return 0;
 /* BuildActor deferred resolution (02A7D0) appends actual target definitions
  * to definition+504. Test capabilities after inheritance, not race/unit names.
  * Both ordinary and sovereign centers use a settlement type (+430) and the
  * marker_settlement requirement. Gardens, mines and foundation camps do not. */
 for(node=P(def,0x504);node&&i++<64;node=P(node,4)){
  U target=P(node,0);
  if(target&&P(target,0x430)&&ids_equal(P(target,0x4e0),"marker_settlement"))return 1;
 }
 return 0;
}
static int route_fighting(U image,U actor){
 U cai=P(actor,0x70),stack=cai?P(cai,0x14):0,state=stack?P(stack,0):0,vt=state?P(state,0)-image:0;
 /* Native combat states, including bombardment/siege and pursuit. Check the
  * members too: a company can still be in Move while its members engage. */
 return vt==0x4f4dc4||vt==0x4f4e20||vt==0x4f4e4c||vt==0x4f4e78||vt==0x4f4ea4||vt==0x4f4fb4;
}
/* Film players deliberately have native +c == 0: the recorded commands replace
 * their strategic controllers. The loaded WorldCreator still retains the
 * serialized kingdom setup (108-byte rows, internal ID at +0, AI profile +2c).
 * Use that original profile only for synchronous routing during film playback.
 * Do not change native player flags, create AI controllers, or guess from names,
 * races, difficulty/handicap, slot numbers or localized display text. */
static U route_string_length(U p){
 U n;if(!p)return 0;n=P(p,-12);
 return n&&n<=128&&((W*)p)[n]==0?n:0;
}
static int route_film_bot(U image,U session,U kingdom){
 U creator=P(session,0x7c),rows,n,i,id=P(kingdom,0x18),len=route_string_length(id),found=0,profile=0;
 if(!creator||P(creator,0)!=image+0x4fdfe4||!len)return 0;
 rows=P(creator,0x14);n=P(creator,0x18);
 if(!rows||!n||n>96||P(creator,0x1c)<n)return 0;
 for(i=0;i<n;i++){
  U row=rows+i*108,key=P(row,0),j;
  if(route_string_length(key)!=len)continue;
  for(j=0;j<len&&((W*)id)[j]==((W*)key)[j];j++);
  if(j!=len)continue;
  if(found)return 0;
  found=1;profile=P(row,0x2c);
 }
 return found&&route_string_length(profile)!=0;
}
/* Session player records exist on every peer. Strategic AI controllers and
 * goals exist only on the host and must never decide a simulation path. The
 * native player type (+c) and assigned live kingdom (+28) are shared slots,
 * also used by the game's player/observer interface after loading a save. */
static U route_bot_player(U image,U kingdom){
 U session=P(image,0x5f3fe4),node,i,found=0;
 if(!session||!kingdom)return 0;
 for(node=P(session,0xc8),i=0;node&&i++<64;node=P(node,4)){
  U p=P(node,0);if(!p||P(p,0)!=image+0x4bd914)return 0;
  if(P(p,0x28)==kingdom){
   if(found)return 0;
   if(P(session,0x64)==2&&P(session,0xf0)==4){
    if(!*(unsigned char*)(p+0xe)||!route_film_bot(image,session,kingdom))return 0;
   }else if(!*(unsigned char*)(p+0xc))return 0;
   found=p;
  }
 }
 return node?0:found;
}
static int route_protected_builder(U image,U actor,U query){
 U org=P(actor,0x7c),list,n,i,target=P(query,0x20);
 int capable=route_center_capable(image,actor);
 if(route_fighting(image,actor))return 0;
 /* Attack paths can start while the low-level company state is still Move.
  * An explicit hostile target bypasses the entire avoidance context, including
  * neighboring guard zones. Only replicated orders and combat states count. */
 if(live_actor(image,target)&&((RouteM1)P(P(target,0),0x144))(target,0,P(actor,0xe8))==3)return 0;
 if(!org||P(org,4)!=actor)return 0;
 list=P(org,0x28);n=P(org,0x2c);if(n>64||(n&&!list))return 0;
 for(i=0;i<n;i++){
  U unit=P(list,4*i),element;
  if(!live_actor(image,unit)||(P(unit,8)&1))continue;
  element=P(unit,0x80);
  if(!element||P(element,4)!=unit||P(element,0x10)!=actor||P(unit,0xe8)!=P(actor,0xe8))continue;
  if(route_fighting(image,unit))return 0;
  if(route_center_capable(image,unit))capable=1;
 }
 return capable;
}
/* The engine constructs company queries with its current leader, not with the
 * company actor (26FA9F -> 24B746). Formation-member queries remain native. */
static U route_company(U image,U actor){
 U element,company,org;
 if(!live_actor(image,actor)||(P(actor,8)&1))return 0;
 if(P(actor,0x7c))return actor;
 element=P(actor,0x80);if(!element||P(element,4)!=actor)return 0;
 company=P(element,0x10);
 if(!live_actor(image,company)||(P(company,8)&1))return 0;
 org=P(company,0x7c);
 return org&&P(org,4)==company&&P(org,0x40)==actor?company:0;
}
static U guarded_structure(U image,U actor){
 U den,part,body;
 if(!live_actor(image,actor)||(P(actor,8)&1))return 0;
 den=P(actor,0x88);part=P(actor,0x94);body=P(actor,0x60);
 if(!den||P(den,0)!=image+0x4e0434||P(den,4)!=actor||!part||P(part,4)!=actor)return 0;
 if(!body||P(body,4)!=actor||!finite(F(body,0x10))||F(body,0x10)<=0)return 0;
 return den;
}
static U structure_center(U image,U actor){
 U settlement,center;
 if(!live_actor(image,actor))return 0;
 settlement=P(actor,0x98);center=settlement?P(settlement,0x14):0;
 return center&&live_actor(image,center)?center:actor;
}
static void route_begin(U image,Data*d,U*args){
 Route*r=&d->route;U query=args[5],a,k,reg,i,target;float own;
 /* Nested calls remain native and cannot overwrite the outer query. */
 if(r->thread==route_thread()&&r->depth&&(U)args<r->frame){r->depth++;return;}
 r->frame=(U)args;
 r->depth=1;r->thread=route_thread();r->count=0;r->changed=0;r->actor=0;
 r->image=image;r->world=P(image,0x5f3fb8);
 if(!query||!r->world||P(image,0x5f9218)!=2)return;
 a=route_company(image,P(query,0x1c));if(!a)return;
 k=P(a,0xe8);if(!k||(r->player=route_bot_player(image,k))==0)return;
 if(!route_protected_builder(image,a,query))return;
 r->sx=*(float*)&args[1];r->sy=*(float*)&args[2];r->tx=*(float*)&args[3];r->ty=*(float*)&args[4];
 if(!finite(r->sx)||!finite(r->sy)||!finite(r->tx)||!finite(r->ty))return;
 r->actor=a;r->kingdom=k;r->builder=1;target=structure_center(image,P(query,0x20));
 own=((RouteF0)(image+0x2274c1))(a,0);if(!finite(own)||own<0)own=0;r->strength=own;
 reg=P(image,0x5ef72c);if(!reg)return;
 for(i=0;i<65536;i++){
  U lair=P(reg,0x20004+4*i),def,cell;float guard,operational,cv,ratio,radius;
  if(!guarded_structure(image,lair))continue;
  /* Native diplomacy including the optional independent-kingdom hostility. */
  if(((RouteM1)P(P(lair,0),0x144))(lair,0,k)!=3)continue;
  /* Native combat can target a guarded structure from offense or defense.
   * Do not turn that intended target into an impassable disk for builders;
   * unrelated threats on construction/exploration routes still count. */
  if(lair==target)continue;
  cell=route_cell(image,F(lair,0x20),F(lair,0x24));if(!cell)continue;
  /* The game's own explored-fog query; never learn hidden lairs by scanning. */
  if(!(((RouteM1)(image+0x24a845))(cell,0,k)&255))continue;
  def=P(lair,4);guard=F(def,0x364);operational=F(def,0x368);
  if(!finite(guard)||!finite(operational)||guard<=0||guard>512||operational>512)continue;
  cv=((RouteF0)(image+0x2274c1))(lair,0);if(!finite(cv)||cv<=0)continue;
  /* These are the same resolved guard/leash ranges checked by the Denizen
   * code, including inherited templates. Small clearance for formation width. */
  radius=maximum(guard,operational)+6;
  ratio=cv/maximum(own,1);
  if(r->count==ROUTE_THREATS){d->counts[18]++;break;}
  RouteThreat*t=&r->threats[r->count++];t->x=F(lair,0x20);t->y=F(lair,0x24);t->radius=radius;t->strength=cv;t->id=P(lair,0x14);
  t->weight=24*minimum(maximum(ratio,1),4);
  t->startDistance2=dist2(r->sx,r->sy,t->x,t->y);
 }
 d->counts[12]++;if(r->count)d->counts[17]++;
}
/* A positive cost penalizes segments intersecting each threat disk.
 * A route already inside a disk can leave without being imprisoned by it.
 * Coincident/zero-length segments and nonfinite input preserve native results. */
static float route_penalty(Route*r,float ax,float ay,float bx,float by){
 float dx=bx-ax,dy=by-ay,len2=dx*dx+dy*dy,sum=0;U i;
 if(!finite(len2)||len2<=0.000001f)return 0;
 for(i=0;i<r->count;i++){
  RouteThreat*t=&r->threats[i];float rr=t->radius*t->radius,u,px,py,d;
  /* Starting inside: permit travel toward the outside of this same danger. */
  if(t->startDistance2<rr&&dist2(bx,by,t->x,t->y)>=dist2(ax,ay,t->x,t->y))continue;
  u=((t->x-ax)*dx+(t->y-ay)*dy)/len2;u=minimum(maximum(u,0),1);
  px=ax+u*dx;py=ay+u*dy;d=dist2(px,py,t->x,t->y);
  if(d>=rr)continue;
  /* sqrt-free positive penalty keeps the native Euclidean heuristic admissible. */
  sum+=t->weight*(1-d/rr)*(1+minimum(len2,4096)*0.0625f);
 }
 return sum;
}
static void route_end(U image,Data*d){
 Route*r=&d->route;if(r->thread!=route_thread()||!r->depth)return;
 if(--r->depth)return;
 if(r->actor&&r->count&&live_actor(image,r->actor)){
  /* Separate last-query mailbox: never race the AI decision ring if an engine
   * path query is made on another thread. Reader detects incomplete copies. */
  volatile RouteReport*s=&r->report;U seq=s->commit+1;s->serial=0;
  s->world=r->world;s->actor=P(r->actor,0x14);s->kingdom=r->kingdom;s->builder=r->builder;
  s->threats=r->count;s->changed=r->changed;s->strength=r->strength;s->x=r->tx;s->y=r->ty;s->time=F(r->world,0xe8);
  s->commit=seq;s->serial=seq;d->counts[11]++;
 }
 r->count=0;r->actor=0;r->thread=0;
}
static void route_evaluate(U image,Data*d,U mode,U obj,U*args,float*result){
 Route*r=&d->route;U grid,world;float ax,ay,bx,by,penalty=0;
 if(mode==11){route_begin(image,d,args);return;}
 if(mode==12){route_end(image,d);return;}
 if(!route_active(image,r))return;
 if(mode==13){
  float step;U g=P(r->world,0x30);
  if(!(*(U*)result&255))return;
  penalty=route_penalty(r,*(float*)&args[0],*(float*)&args[1],*(float*)&args[2],*(float*)&args[3]);
  step=g?F(g,8):0;
  /* Reject a long straight shortcut through danger, so the weighted solver
   * gets to choose a detour. Adjacent grid steps stay legal: an unavoidable
   * narrow passage or a destination inside a guard radius is not a wall. */
  if(penalty>0&&finite(step)&&step>0&&dist2(*(float*)&args[0],*(float*)&args[1],*(float*)&args[2],*(float*)&args[3])>step*step*2.01f){*(U*)result&=0xffffff00;r->changed++;d->counts[13]++;}return;
 }
 world=r->world;grid=P(world,0x30);if(!grid||P(grid,4)>8)return;
 if(mode==14)return; /* Terrain/object collision is native; danger adds cost. */
 if(mode==15){
  U node=args[0],shift=P(grid,4);float half=F(grid,8)*0.5f;
  ax=(float)((int)P(node,0)<<shift)+half;ay=(float)((int)P(node,4)<<shift)+half;
  bx=(float)((int)args[1]<<shift)+half;by=(float)((int)args[2]<<shift)+half;
 }else if(mode==16){
  U map=P(image,0x5f3fcc),to,n,list;if(!map)return;n=P(map,0x18);list=P(map,0x14);if(!list||args[0]>=n||!obj)return;
  to=P(list,args[0]*4);if(!to)return;ax=F(obj,0x54);ay=F(obj,0x58);bx=F(to,0x54);by=F(to,0x58);
 }else return;
 penalty=route_penalty(r,ax,ay,bx,by);
 if(penalty>0&&finite(*result)&&finite(*result+penalty)){*result+=penalty;r->changed++;d->counts[mode]++;}
}
