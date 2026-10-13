import assert from 'node:assert/strict';

export async function configurationTests(db, login, rpc, user, peer) {
 let checks=0; const check=(ok,msg)=>{assert.ok(ok,msg);checks++;};
 const ready=async()=>{await db.exec('reset role');await db.query("update paw_private.social_presence set seen_at=now()-interval '5 seconds' where player_id=$1",[user]);await login(user);};
 const valid=[],invalid=[];
 for(const mod of ['VANILLA','IMMORTALS']) for(const channel of ['STABLE','BETA'])
 for(let mask=0;mask<32;mask++) {
  const patch=!!(mask&1),colors=!!(mask&2),desync=!!(mask&4),data=!!(mask&8),ru=!!(mask&16);
  const code=`PAW-${channel}-${mod}${ru?'-RU1':''}${patch?'-PP1':''}${colors?'-CL1':''}${desync?'-OOS1':''}${data?'-DATA':''}`;
  const supported=(!data||patch||mod==='IMMORTALS')&&(!(colors||desync)||(patch&&!data));
  if(!supported){invalid.push(code);continue;}
  valid.push(code);
  await ready();
  const versions={launcher:'0.8.5.0',mod:mod.toLowerCase(),channel:channel.toLowerCase(),content_id:'B'.repeat(64),patch:patch?'0.3.0-beta.1':null};
  check((await rpc('paw_presence',[false,channel.toLowerCase(),{core:!patch,colors:!colors,desync:!desync,_versions:versions},code])).status==='ok','accepted '+code);
  await login(peer);
  const player=(await rpc('paw_social_list',[])).players.find(p=>p.id===user);
  check(player.configuration===code,'full code preserved '+code);
  check(player.components.core===patch&&player.components.colors===colors&&player.components.desync===desync,'server derives real flags '+code);
  check(player.versions.mod===mod.toLowerCase()&&player.versions.launcher==='0.8.5.0','mod/version metadata preserved '+code);
 }
 await ready();
 for(const code of [...invalid,'PAW-BETA-VANILLA-PP1-CL1-OOS1\n','PAW-BETA-VANILLA-PP1-URL-file','PAW-BETA-VANILLA-PP1-CL1-CL1',
  'PAW-BETA-IW1-SP4-RM1-SG1-LM0-RU0-CL1-OOS1','PAW-BETA-IW1-SP4-RM1-SG1-LM1-RU0-CL1-OOS1-DATA'])
  check((await rpc('paw_presence',[false,code.includes('-STABLE-')?'stable':'beta',{},code])).status==='invalid_presence','reject '+code);
 check((await rpc('paw_presence',[false,'stable',{},'PAW-BETA-VANILLA-PP1-CL1'])).status==='invalid_presence','channel mismatch rejected');
 await db.exec('reset role');
 for(const code of valid) for(const suffix of ['', '-TXUK-VORU','-TXCS-VOEN','-TXDE-VODE','-TXFR-VOFR'])
  check((await db.query('select paw_private.valid_social_configuration($1) ok',[code+suffix])).rows[0].ok,'language suffix '+code+suffix);
 // Arcane modes and limits remain compatible, including old explicit PS1.
 for(const channel of ['STABLE','BETA']) for(const spawn of [1,2,4]) for(let mask=0;mask<128;mask++) {
  const code=`PAW-${channel}-IW${mask&1?1:0}-SP${spawn}-RM${mask&2?1:0}-SG${mask&4?1:0}-LM1-RU${mask&8?1:0}-CL${mask&16?1:0}-OOS${mask&32?1:0}${mask&64?'-PS1':''}`;
  check((await db.query('select paw_private.valid_social_configuration($1) ok',[code])).rows[0].ok,'Arcane preserved '+code);
 }
 // AI flag is bounded to the full Arcane patch in both channels. Projection ignores spoofed booleans.
 const aiCode='PAW-BETA-IW1-SP4-RM1-SG1-LM1-RU0-CL1-OOS1-AI1';
 for(const channel of ['beta','stable']) for(const suffix of ['', '-TXUK-VORU']) {
  const code=aiCode.replace('BETA',channel.toUpperCase())+suffix;
  await ready();check((await rpc('paw_presence',[false,channel,{improved_ai:false},code])).status==='ok','AI presence accepted '+channel);
  await login(peer);const p=(await rpc('paw_social_list',[])).players.find(p=>p.id===user);
  check(p.components.improved_ai===true&&p.configuration===code,'AI flag projected from code');
 }
 await ready();
 for(const code of [aiCode+'-PP0',aiCode+'-DATA',aiCode.replace('BETA','STABLE')+'-PP0',aiCode.replace('BETA','STABLE')+'-DATA','PAW-BETA-VANILLA-PP1-AI1',aiCode+'-AI1'])
  check((await rpc('paw_presence',[false,code.includes('-STABLE-')?'stable':'beta',{},code])).status==='invalid_presence','AI invalid combination rejected');
 check((await rpc('paw_offer_create',[peer,'a0850000-0000-0000-0000-000000000005','config',aiCode,null,null,null])).status!=='invalid_offer','AI offer passes validation');
 check((await rpc('paw_offer_create',[peer,'a0850000-0000-0000-0000-000000000006','config',aiCode.replace('BETA','STABLE'),null,null,null])).status!=='invalid_offer','Stable AI offer passes validation');
 // Keep this extra fixture offer outside the five-per-minute budget used below.
 await db.exec('reset role');
 await db.exec("update paw_private.social_offers set created_at=now()-interval '2 minutes' where id='a0850000-0000-0000-0000-000000000006'");
 await login(user);
 // Exercise the actual offer RPC, idempotence and duplicate-config guard in the fixture only.
 for(const mod of ['VANILLA','IMMORTALS']) for(const channel of ['STABLE','BETA']) {
  await db.exec('reset role');
  await db.query('update paw_private.social_presence set configuration=null,versions=null where player_id=$1',[peer]);
  await login(user);
  const code=`PAW-${channel}-${mod}-PP1-CL1-OOS1`;
  const suffix=(channel==='STABLE'?10:0)+(mod==='VANILLA'?1:2);
  const id='a0850000-0000-0000-0000-'+String(suffix).padStart(12,'0');
  check((await rpc('paw_offer_create',[peer,id,'config',code,null,null,null])).status==='ok','offer accepted '+mod);
  check((await rpc('paw_offer_create',[peer,id,'config',code,null,null,null])).status==='ok','offer retry '+mod);
  await db.exec('reset role');
  await db.query("update paw_private.social_presence set seen_at=now()-interval '5 seconds' where player_id=$1",[peer]);
  await login(peer);
  check((await rpc('paw_presence',[false,channel.toLowerCase(),{},code.replace('-PP1','-RU0-PP1')])).status==='ok','recipient applies matching configuration');
  await login(user);
  check((await rpc('paw_offer_create',[peer,id.replace(/.$/,'3'),'config',code,null,null,null])).status==='configuration_matches','equivalent offer denied '+mod);
 }
 check((await rpc('paw_offer_create',[peer,'a0850000-0000-0000-0000-000000000004','config','PAW-STABLE-VANILLA-CL1',null,null,null])).status==='invalid_offer','invalid offer denied');
 // Leave the shared fixtures as they were before this suite.
 await db.exec('reset role');
 for(const channel of ['STABLE','BETA']) for(const mod of ['VANILLA','IMMORTALS']) for(const spawn of [1,2,4]) for(let mask=0;mask<16;mask++) {
  const code=`PAW-${channel}-${mod}-PP1-PB1-AI${mask&1?1:0}-LR${mask&2?1:0}-IW${mask&4?1:0}-SP${spawn}-RM${mask&8?1:0}`;
  for(const suffix of ['', '-TXUK-VORU']) check((await db.query('select paw_private.valid_social_configuration($1) ok',[code+suffix])).rows[0].ok,'Pure beta '+code+suffix);
  for(const bad of [code.replace('-PP1',''),code+'-DATA'])
   check(!(await db.query('select paw_private.valid_social_configuration($1) ok',[bad])).rows[0].ok,'Pure beta restriction '+bad);
 }
 for(const channel of ['stable','beta']) for(const mod of ['VANILLA','IMMORTALS']) {
  const code=`PAW-${channel.toUpperCase()}-${mod}-PP1-PB1-AI1-LR1-IW1-SP2-RM1`;
  await ready(); check((await rpc('paw_presence',[false,channel,{improved_ai:false,lair_recovery:false},code])).status==='ok','Pure channel presence');
  await login(peer); const p=(await rpc('paw_social_list',[])).players.find(p=>p.id===user);
  check(p.components.improved_ai&&p.components.lair_recovery&&p.components.hostility&&p.components.roaming&&p.components.additional_roaming&&p.components.large_maps,'derive Pure beta flags from code');
  await db.exec('reset role');
  await db.query('update paw_private.social_presence set configuration=$1 where player_id=$2',[code.replace('-PP1','-RU0-PP1'),peer]);
  await login(user);
  check((await rpc('paw_offer_create',[peer,'a0850000-0000-0000-0000-000000000099','config',code,null,null,null])).status==='configuration_matches','Pure beta equivalent offer denied '+mod);
 }
 await db.exec('reset role');
 await db.exec("delete from public.paw_messages where message_id::text like 'a085%'; delete from paw_private.social_offers where id::text like 'a085%';");
 check(!(await db.query("select has_function_privilege('anon','paw_private.valid_social_configuration(text)','execute') or has_function_privilege('authenticated','paw_private.valid_social_configuration(text)','execute') exposed")).rows[0].exposed,'private helper stays private');
 await ready();await rpc('paw_presence',[false,'beta',{core:true},'PAW-BETA-IW1-SP4-RM1-SG1-LM1-RU0-CL0-OOS0']);
 console.log('SOCIAL CONFIGURATION DATABASE:',checks,'PASS');return checks;
}
