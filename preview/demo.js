'use strict';
// Public showcase only. All mutations stay in memory; nothing is sent to a backend or browser storage.
(() => {
  const assetVersion=document.currentScript?new URL(document.currentScript.src).search:'';
  const $=s=>document.querySelector(s), clone=x=>structuredClone(x);
  const iso=d=>d.toISOString().slice(0,10), date=s=>new Date(s+'T12:00:00Z');
  const add=(s,n)=>{const d=date(s);d.setUTCDate(d.getUTCDate()+n);return iso(d);};
  const week=s=>add(s,-((date(s).getUTCDay()+6)%7));
  const age=(birth,day)=>Number(day.slice(0,4))-Number(birth.slice(0,4))-(day.slice(5)<birth.slice(5)?1:0);
  const yearAgo=s=>{const d=date(s),month=d.getUTCMonth();d.setUTCFullYear(d.getUTCFullYear()-1);if(d.getUTCMonth()!==month)d.setUTCDate(0);return iso(d);};
  const days=(a,b)=>{const result=[];for(let d=a;d<=b;d=add(d,1))result.push(d);return result;};
  const sites=[{siteId:1,name:'Gelsenkirchen'},{siteId:2,name:'Bottrop'}];
  const genders={Female:'Weiblich',Male:'Männlich',Diverse:'Divers',Unspecified:'Keine Angabe'};
  const weekdays=['Sonntag','Montag','Dienstag','Mittwoch','Donnerstag','Freitag','Samstag'];
  let seed, children, attendances, archives=[], accounts, role='manager';
  function fail(message,status=400){const error=new Error(message);error.status=status;throw error;}
  const member=()=>accounts.find(a=>a.id==='demo-'+role);
  function allow(siteId,manager=false){if(member().isAdmin&&sites.some(s=>s.siteId===siteId))return{...sites.find(s=>s.siteId===siteId),role:'Manager'};const grant=member().sites.find(s=>s.siteId===siteId);if(!grant||manager&&grant.role!=='Manager')fail('Keine Berechtigung für diesen Standort.',403);return grant;}
  function childBy(id){const c=children.find(x=>x.id===id);if(!c)fail('Kind nicht gefunden.',404);allow(c.siteId);return c;}
  function reset(){children=clone(seed.children);attendances=clone(seed.attendances);archives=[];accounts=[{id:'demo-manager',displayName:'Alex · Hausleitung',email:'hausleitung@example.invalid',isAdmin:false,isBlocked:false,sites:sites.map(s=>({...s,role:'Manager'}))},{id:'demo-employee',displayName:'Sam · Mitarbeitende',email:'team@example.invalid',isAdmin:false,isBlocked:false,sites:[{...sites[0],role:'Employee'}]},{id:'demo-admin',displayName:'Zentrale Administration',email:'zentrale@example.invalid',isAdmin:true,isBlocked:false,sites:sites.map(s=>({...s,role:'Manager'}))}];}
  function view(c){const entries=attendances.filter(a=>a.childId===c.id),today=entries.find(a=>a.day===seed.today),last=entries.map(a=>a.day).sort().at(-1);return {...c,age:age(c.birthDate,seed.today),lastVisit:last??null,inactive:(last??c.createdOn??add(seed.today,-490))<yearAgo(seed.today),present:!!today,attendanceId:today?.id,canUndo:!!today&&(today.createdBy===member().id||allow(c.siteId).role==='Manager')};}
  function save(id,input){
    allow(input.siteId);if(!input.firstName?.trim()||!input.lastName?.trim())fail('Vor- und Nachname sind Pflicht.');
    if(!/^\d{4}-\d\d-\d\d$/.test(input.birthDate)||input.birthDate>seed.today||age(input.birthDate,seed.today)>100)fail('Bitte ein gültiges Geburtsdatum eingeben.');
    if(!genders[input.gender])fail('Bitte ein gültiges Geschlecht auswählen.');
    if(!input.nationalities?.length||input.nationalities.some(code=>!seed.countries.some(c=>c.code===code)))fail('Bitte mindestens eine Staatsangehörigkeit auswählen.');
    const contact=[input.contactName,input.contactPhone,input.contactRelationship];if(contact.some(Boolean)&&!contact.every(x=>x?.trim()))fail('Bitte den Notfallkontakt vollständig ausfüllen oder alle drei Felder leer lassen.');
    if(input.contactPhone&&!/^[+0-9 ()/.-]{5,40}$/.test(input.contactPhone))fail('Bitte die Telefonnummer prüfen.');
    if(!input.confirmDuplicate&&children.some(c=>c.id!==id&&c.siteId===input.siteId&&c.birthDate===input.birthDate&&c.firstName.toLowerCase()===input.firstName.trim().toLowerCase()&&c.lastName.toLowerCase()===input.lastName.trim().toLowerCase()))fail('Ein Kind mit diesem Namen und Geburtsdatum existiert bereits. Bitte prüfen und gegebenenfalls als anderes Kind bestätigen.',409);
    const old=id?childBy(id):null;if(old&&old.revision!==input.revision)fail('Die Daten wurden inzwischen geändert. Bitte neu laden.',409);
    const c={...input,id:id??crypto.randomUUID(),revision:crypto.randomUUID(),createdOn:old?.createdOn??seed.today,firstName:input.firstName.trim(),lastName:input.lastName.trim(),nationalities:[...new Set(input.nationalities)]};
    if(old)children[children.indexOf(old)]=c;else children.push(c);return{id:c.id,revision:c.revision};
  }
  function slice(f,start,end){
    const entries=attendances.filter(a=>a.day>=start&&a.day<=end&&f.siteIds.includes(a.siteId)).map(a=>({...a,child:children.find(c=>c.id===a.childId)})).filter(a=>{const n=age(a.child.birthDate,a.day);return(f.minAge==null||n>=f.minAge)&&(f.maxAge==null||n<=f.maxAge)&&(!f.genders?.length||f.genders.includes(a.child.gender))&&(!f.nationalities?.length||a.child.nationalities.some(c=>f.nationalities.includes(c)))&&(f.weekday==null||date(a.day).getUTCDay()===f.weekday);});
    const selectedDays=days(start,end),denominator=selectedDays.filter(d=>f.weekday==null||date(d).getUTCDay()===f.weekday).length;
    const archive=archives.filter(a=>f.siteIds.includes(a.siteId)&&a.month>=start.slice(0,7)+'-01'&&a.month<=end.slice(0,7)+'-01');
    const detail=f.minAge!=null||f.maxAge!=null||f.weekday!=null||f.genders?.length||f.nationalities?.length;
    const monthEnd=s=>{const d=date(s);d.setUTCMonth(d.getUTCMonth()+1);d.setUTCDate(0);return iso(d);};
    const complete=!archive.length||!detail&&archive.every(a=>start<=a.month&&end>=monthEnd(a.month));
    const childComplete=!archive.length||!detail&&start.endsWith('-01')&&end===monthEnd(start);
    const visits=complete?entries.length+archive.reduce((n,a)=>n+a.visits,0):null;
    const time=d=>f.grouping==='week'?week(d):f.grouping==='month'?d.slice(0,7)+'-01':d;
    const bucket=(key,label,test)=>({key:String(key),label,value:entries.filter(test).length});
    return {start,end,metrics:{visits,children:childComplete?new Set(entries.map(a=>a.childId)).size+archive.reduce((n,a)=>n+a.profiles,0):null,average:visits!=null&&denominator?Math.round(visits/denominator*100)/100:null,calendarDays:denominator},hasArchive:!!archive.length,notice:archive.length?'Nach Profillöschungen liegen zusätzlich anonyme Monatssummen vor. Diagramme zeigen nur erhaltene Einzelanwesenheiten. Nicht vollständig berechenbare Kennzahlen werden nicht ausgewiesen.':null,
      timeline:[...new Set(selectedDays.map(time))].map(d=>bucket(d,f.grouping==='month'?date(d).toLocaleDateString('de-DE',{month:'short',year:'2-digit'}):d.slice(8,10)+'.'+d.slice(5,7)+'.',a=>time(a.day)===d)),
      weekdays:[1,2,3,4,5,6,0].map(n=>bucket(n,weekdays[n].slice(0,2),a=>date(a.day).getUTCDay()===n)),
      ages:[[0,5],[6,9],[10,14],[15,17],[18,100]].map(([lo,hi])=>bucket(lo+':'+hi,hi===100?'18+ Jahre':lo+'–'+hi+' Jahre',a=>age(a.child.birthDate,a.day)>=lo&&age(a.child.birthDate,a.day)<=hi)),
      genders:Object.entries(genders).map(([g,label])=>bucket(g,label,a=>a.child.gender===g)),
      nationalities:[...new Set(entries.flatMap(a=>a.child.nationalities))].map(code=>bucket(code,seed.countries.find(c=>c.code===code)?.name??code,a=>a.child.nationalities.includes(code))).sort((a,b)=>b.value-a.value||a.label.localeCompare(b.label,'de')),
      sites:f.siteIds.map(id=>bucket(id,sites.find(s=>s.siteId===id).name,a=>a.siteId===id)),archive};
  }
  function report(input){
    const f={minAge:null,maxAge:null,genders:[],nationalities:[],weekday:null,grouping:'day',compare:true,compareStart:null,compareEnd:null,preset:'custom',...clone(input)};
    f.siteIds=f.siteIds?.length?f.siteIds:member().sites.map(s=>s.siteId);if(!f.siteIds.length)fail('Kein Standort zugewiesen.',403);f.siteIds.forEach(id=>allow(id));
    const valid=(a,b)=>/^\d{4}-\d\d-\d\d$/.test(a)&&/^\d{4}-\d\d-\d\d$/.test(b)&&Number.isFinite(+date(a))&&Number.isFinite(+date(b))&&a<=b&&b<=seed.today&&(+date(b)-date(a))/86400000<=3660;
    if(!valid(f.start,f.end))fail('Bitte einen gültigen Zeitraum bis zum Demotag auswählen (höchstens zehn Jahre).');
    if(f.minAge!=null&&(f.minAge<0||f.minAge>100)||f.maxAge!=null&&(f.maxAge<0||f.maxAge>100)||f.minAge!=null&&f.maxAge!=null&&f.minAge>f.maxAge)fail('Bitte den Altersbereich prüfen.');
    let comparison=null;
    if(f.compare){const length=(date(f.end)-date(f.start))/86400000+1,offset=f.preset==='week'&&f.start===week(seed.today)&&f.end===seed.today?7:length;const a=f.compareStart||add(f.start,-offset),b=f.compareEnd||add(f.end,-offset);if(!valid(a,b))fail('Der Vergleichszeitraum ist ungültig.');comparison=slice(f,a,b);}
    const labels=[f.start+' – '+f.end,f.siteIds.map(id=>sites.find(s=>s.siteId===id).name).join(', ')];
    if(f.minAge!=null||f.maxAge!=null)labels.push(`Alter: ${f.minAge??0}–${f.maxAge??100} Jahre am Besuchstag`);
    if(f.genders.length)labels.push('Geschlecht: '+f.genders.map(g=>genders[g]).join(', '));
    if(f.nationalities.length)labels.push('Staatsangehörigkeit: '+f.nationalities.map(c=>seed.countries.find(x=>x.code===c)?.name).join(', '));
    if(f.weekday!=null)labels.push('Wochentag: '+weekdays[f.weekday]);
    if(comparison)labels.push('Vergleich: '+comparison.start+' – '+comparison.end);
    return{filter:f,filterLabels:labels,crossSite:f.siteIds.length>1,current:slice(f,f.start,f.end),comparison};
  }
  async function request(path,method='GET',input){
    const url=new URL(path,'https://demo.invalid'),parts=url.pathname.split('/').filter(Boolean);
    if(path==='/session')return clone({...member(),sites:member().sites.map(s=>({...s,name:sites.find(x=>x.siteId===s.siteId)?.name})),demo:true,today:seed.today,countries:seed.countries});
    if(parts[0]==='reports'){for(const siteId of input.siteIds??[])allow(siteId,true);const result=report(input);return parts[1]==='export'?window.demoExcel(result):result;}
    if(parts[0]==='children'){
      const id=parts[1];
      if(!id&&method==='GET'){const site=Number(url.searchParams.get('siteId'));allow(site);return children.filter(c=>c.siteId===site).map(view).filter(c=>url.searchParams.get('inactive')==='true'||!c.inactive).sort((a,b)=>a.lastName.localeCompare(b.lastName,'de')||a.firstName.localeCompare(b.firstName,'de'));}
      if(parts[2]==='attendance'){const c=childBy(id);if(method==='GET'){allow(c.siteId,true);return clone(attendances.filter(a=>a.childId===id).sort((a,b)=>b.day.localeCompare(a.day)));}allow(c.siteId,input.day!==seed.today);if(input.day>seed.today||input.day<c.birthDate)fail('Dieses Anwesenheitsdatum ist nicht gültig.');const existing=attendances.find(a=>a.childId===id&&a.day===input.day);if(existing)return{id:existing.id,alreadyPresent:true};const a={id:crypto.randomUUID(),childId:id,siteId:c.siteId,day:input.day,createdBy:member().id};attendances.push(a);c.revision=crypto.randomUUID();return{id:a.id,alreadyPresent:false};}
      if(method==='POST'||method==='PUT')return save(id,input);
      if(method==='DELETE'){const c=childBy(id);allow(c.siteId,true);if(url.searchParams.get('preserve')==='true'){const entries=attendances.filter(a=>a.childId===id);for(const month of new Set(entries.map(a=>a.day.slice(0,7)+'-01'))){let a=archives.find(x=>x.siteId===c.siteId&&x.month===month);if(!a){a={siteId:c.siteId,site:sites.find(s=>s.siteId===c.siteId).name,month,visits:0,profiles:0};archives.push(a);}a.visits+=entries.filter(x=>x.day.startsWith(month.slice(0,7))).length;a.profiles++;}}children=children.filter(x=>x.id!==id);attendances=attendances.filter(a=>a.childId!==id);return null;}
    }
    if(parts[0]==='attendance'&&method==='DELETE'){const a=attendances.find(x=>x.id===parts[1]);if(!a)fail('Anwesenheit nicht gefunden.',404);const grant=allow(a.siteId);if(grant.role!=='Manager'&&(a.createdBy!==member().id||a.day!==seed.today))fail('Nur eigene heutige Einträge können zurückgenommen werden.',403);attendances=attendances.filter(x=>x.id!==a.id);return null;}
    if(parts[0]==='admin'){
      if(!member().isAdmin)fail('Keine Berechtigung.',403);if(method==='GET')return clone(accounts);
      if(parts[2]===member().id)fail('Die eigenen Administrationsrechte können hier nicht verändert werden.');
      if(parts[3]==='reset')return{activationPath:location.pathname+'?page=account'};
      if(method==='POST'){if(accounts.some(a=>a.email===input.email))fail('Diese E-Mail-Adresse existiert bereits.');accounts.push({id:crypto.randomUUID(),displayName:input.name,email:input.email,isAdmin:input.isAdmin,isBlocked:false,sites:input.isAdmin?sites.map(s=>({...s,role:'Manager'})):clone(input.sites)});return{activationPath:location.pathname+'?page=account'};}
      const a=accounts.find(x=>x.id===parts[2]);if(!a)fail('Zugang nicht gefunden.',404);Object.assign(a,{isAdmin:input.isAdmin,isBlocked:input.isBlocked,sites:input.isAdmin?sites.map(s=>({...s,role:'Manager'})):clone(input.sites)});return null;
    }
    fail('Diese Funktion ist in der öffentlichen Demo nicht verfügbar.');
  }
  function route(page='account',nextRole=role,site='1',push=true){
    if(!seed)return;window.neuerKidsDemo.dispose?.();role=['manager','employee','admin'].includes(nextRole)?nextRole:'manager';window.neuerKidsDemo.role=role;
    if(!['account','today','children','dashboard','admin'].includes(page))page='account';
    if(role==='employee'&&page==='dashboard')page='today';
    if(push)history.pushState({},'',`?page=${page}&role=${role}&site=${site}`);
    document.body.dataset.ready=page==='account'?'true':'false';$('#toast').hidden=true;document.body.className=page==='account'?'auth-shell':'app-shell';document.body.dataset.page=page;
    $('#demo-root').replaceChildren((page==='account'?$('#login-template'):$('#shell-template')).content.cloneNode(true));
    if(page!=='account'){
      $('#main').replaceChildren($('#page-'+page).content.cloneNode(true));
      $('#demo-role').value=role;
      $('#demo-role').addEventListener('change',e=>{const next=e.target.value;route(next==='admin'?'admin':page==='admin'?'today':page,next,'1');});
      const script=document.createElement('script');script.src='assets/js/app.js'+assetVersion;script.onload=()=>script.remove();script.onerror=()=>{$('#main').textContent='Die Vorschau konnte nicht geladen werden. Bitte neu laden.';};document.body.append(script);
    }
    document.title=(page==='account'?'Demo':({today:'Heute',children:'Kinder',dashboard:'Auswertungen',admin:'Verwaltung'})[page])+' · NEUER KIDS';scrollTo(0,0);
  }
  document.addEventListener('submit',e=>{if(e.target.id==='demo-logout'){e.preventDefault();route('account');}});
  document.addEventListener('click',e=>{
    const roleButton=e.target.closest('[data-role]');if(roleButton){route(roleButton.dataset.role==='admin'?'admin':'today',roleButton.dataset.role);return;}
    if(e.target.closest('#demo-reset')){reset();route(document.body.dataset.page,role,$('#site-picker')?.value||'1');return;}
    const a=e.target.closest('a');if(!a||e.metaKey||e.ctrlKey||e.shiftKey||e.altKey)return;
    if(a.hasAttribute('data-nav')||a.closest('.brand,.mobile-brand')||a.getAttribute('href')?.startsWith('?page=')){e.preventDefault();const params=new URL(a.href).searchParams;route(a.dataset.nav||params.get('page')||'today',params.get('role')||role,params.get('site')||$('#site-picker')?.value||'1');}
  });
  addEventListener('popstate',()=>{const q=new URLSearchParams(location.search);route(q.get('page')||'account',q.get('role')||role,q.get('site')||'1',false);});
  window.neuerKidsDemo={request,role};
  fetch('fixtures.json',{cache:'no-store'}).then(r=>{if(!r.ok)throw new Error('Beispieldaten nicht erreichbar.');return r.json();}).then(data=>{if(data.synthetic!==true)throw new Error('Ungültige Beispieldaten.');seed=data;reset();const q=new URLSearchParams(location.search);route(q.get('page')||'account',q.get('role')||'manager',q.get('site')||'1',false);}).catch(()=>{const el=$('#boot-error');if(el){el.hidden=false;el.textContent='Die Beispieldaten konnten nicht geladen werden. Bitte die Seite neu laden.';}});
})();
