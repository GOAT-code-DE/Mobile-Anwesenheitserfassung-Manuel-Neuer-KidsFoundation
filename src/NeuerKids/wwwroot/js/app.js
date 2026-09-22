'use strict';
(() => {
  const $ = (s, root = document) => root.querySelector(s);
  const $$ = (s, root = document) => [...root.querySelectorAll(s)];
  const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const page = document.body.dataset.page;
  const csrf = $('meta[name="csrf-token"]').content;
  const number = value => new Intl.NumberFormat('de-DE', {maximumFractionDigits:1}).format(value);
  const date = value => new Date(value + 'T12:00:00Z').toLocaleDateString('de-DE');
  const iso = d => d.toISOString().slice(0,10);
  const addDays = (value, days) => { const d = new Date(value + 'T12:00:00Z'); d.setUTCDate(d.getUTCDate() + days); return iso(d); };
  const weekday = value => new Date(value + 'T12:00:00Z').getUTCDay();
  let session, siteId, children = [], toastTimer, filter, report, queryVersion = 0, searchTimer, searchRevealTimer, timeSelection = null;
  const countrySets = {};

  async function api(path, method = 'GET', data, blob = false) {
    let response;
    try { response = await fetch('/api' + path, {method, credentials:'same-origin', cache:'no-store', headers:{'Content-Type':'application/json','X-CSRF-TOKEN':csrf}, ...(data !== undefined ? {body:JSON.stringify(data)} : {})}); }
    catch { throw new Error('Keine Verbindung. Es wurde keine Speicherung bestätigt. Bitte Verbindung prüfen und erneut versuchen.'); }
    if (response.status === 401) { location.assign('/Account'); throw new Error('Bitte erneut anmelden.'); }
    if (!response.ok) { const body = await response.json().catch(() => ({})); const error = new Error(body.error || (response.status === 429 ? 'Zu viele Anfragen. Bitte kurz warten.' : 'Die Anfrage konnte nicht abgeschlossen werden.')); error.status = response.status; throw error; }
    if (response.status === 204) return null;
    return blob ? response.blob() : response.json();
  }
  function toast(text, error = false) { const el = $('#toast'); clearTimeout(toastTimer); el.textContent = text; el.classList.toggle('error',error); el.hidden = false; toastTimer = setTimeout(() => el.hidden = true, error ? 8000 : 4500); }
  function errorBox(selector, message) { const el = $(selector); el.hidden = !message; el.textContent = message || ''; }
  async function busy(button, action) { if (button.disabled) return; button.disabled = true; try { await action(); } catch(e) { toast(e.message,true); } finally { button.disabled = false; } }
  function countryName(code) { return session.countries.find(c => c.code === code)?.name || code; }
  function manager() { return session.sites.find(s => s.siteId === siteId)?.role === 'Manager'; }
  function countryPicker(id, values = []) {
    const root = document.getElementById(id); countrySets[id] = new Set(values);
    root.innerHTML = `<select aria-label="${id === 'child-countries' ? 'Staatsangehörigkeit hinzufügen' : 'Staatsangehörigkeit filtern'}"><option value="">Land auswählen …</option>${session.countries.map(c => `<option value="${esc(c.code)}">${esc(c.name)}</option>`).join('')}</select><div class="country-tags"></div>`;
    const render = () => { $('.country-tags',root).innerHTML = [...countrySets[id]].map(c => `<span class="filter-chip">${esc(countryName(c))}<button type="button" data-country="${esc(c)}" aria-label="${esc(countryName(c))} entfernen">×</button></span>`).join(''); };
    $('select',root).addEventListener('change', e => { if (e.target.value) countrySets[id].add(e.target.value); e.target.value = ''; render(); });
    $('.country-tags',root).addEventListener('click', e => { const b = e.target.closest('[data-country]'); if (b) { countrySets[id].delete(b.dataset.country); render(); } }); render();
  }
  function setNav() {
    $$('[data-nav]').forEach(a => { const active = a.dataset.nav === page; a.classList.toggle('active',active); if(active) a.setAttribute('aria-current','page'); if(siteId) a.href = a.pathname + '?site=' + siteId; });
  }
  document.addEventListener('click', e => { const close = e.target.closest('[data-close]'); if(close) document.getElementById(close.dataset.close).close(); });
  window.addEventListener('offline', () => toast('Keine Internetverbindung. Erfassungen können derzeit nicht gespeichert werden.',true));
  window.addEventListener('online', () => toast('Die Verbindung ist wieder da. Nicht bestätigte Erfassungen bitte erneut prüfen.'));

  async function loadChildren() {
    const version = ++queryVersion;
    try {
      const list = await api(`/children?siteId=${siteId}&inactive=${$('#include-inactive').checked}`);
      if (version !== queryVersion) return;
      children = list; renderChildren();
    } catch(e) { if(version !== queryVersion) return; $('#children-list').innerHTML = `<div class="notice error">${esc(e.message)} <button class="button" data-action="retry-children">Erneut laden</button></div>`; }
  }
  function renderChildren() {
    const search = $('#child-search').value.trim().toLocaleLowerCase('de-DE');
    let list = children.filter(c => `${c.firstName} ${c.lastName}`.toLocaleLowerCase('de-DE').includes(search));
    if(page === 'today') {
      $('#today-count').textContent = children.filter(c => c.present).length;
      $('#list-heading').textContent = search || $('#include-inactive').checked ? 'Kinder finden' : 'Heute im Haus';
      if(!search && !$('#include-inactive').checked) list = list.filter(c => c.present);
    }
    $('#list-caption').textContent = `${list.length} ${list.length === 1 ? 'Kind' : 'Kinder'}`;
    $('#children-list').innerHTML = list.length ? list.map(c => `<article class="child-row"><div class="child-avatar" aria-hidden="true">${esc(c.firstName[0] + c.lastName[0])}</div><div class="child-main"><button type="button" data-edit="${c.id}">${esc(c.firstName)} ${esc(c.lastName)}</button><div class="child-meta"><span>${c.age} Jahre</span><span>·</span><span>${date(c.birthDate)}</span>${c.inactive ? '<span class="archive-badge">Inaktiv</span>' : ''}</div>${c.contactPhone ? `<a class="contact-link" href="tel:${esc(c.contactPhone.replace(/[^+0-9]/g,''))}">${esc(c.contactName)} · ${esc(c.contactPhone)}</a>` : ''}</div><div class="child-actions">${c.present ? `<span class="presence-badge">Heute erfasst</span>${c.canUndo ? `<button type="button" class="button small-button" data-undo="${c.attendanceId}" aria-label="Anwesenheit von ${esc(c.firstName)} zurücknehmen">Zurücknehmen</button>` : ''}` : `<button type="button" class="button small-button primary" data-attend="${c.id}">Heute erfassen</button>`}<button type="button" class="button small-button" data-edit="${c.id}" aria-label="Profil von ${esc(c.firstName)} bearbeiten">Bearbeiten</button></div></article>`).join('') : `<div class="empty-state">${search ? 'Kein passendes Kind gefunden. Prüfe die Schreibweise oder beziehe inaktive Kinder ein.' : page === 'today' ? 'Noch kein Kind für heute erfasst. Suche oben nach einem Namen oder lege ein neues Kind an.' : 'Hier sind noch keine Kinder angelegt.'}</div>`;
  }
  async function openChild(id) {
    const child = children.find(c => c.id === id); const form = $('#child-form'); form.reset();
    $('#child-dialog-title').textContent = child ? `${child.firstName} ${child.lastName}` : 'Kind anlegen';
    for(const field of ['id','revision','firstName','lastName','birthDate','gender','contactName','contactPhone','contactRelationship']) form.elements[field].value = child?.[field] ?? '';
    form.elements.birthDate.max = session.today; form.elements.historyDay.value = session.today; form.elements.historyDay.max = session.today;
    countryPicker('child-countries',child?.nationalities ?? []); $('#duplicate-confirm').hidden = true; errorBox('#child-error','');
    $('#save-and-attend').hidden = !!child; $('#delete-section').hidden = !child || !manager(); $('#history-section').hidden = !child || !manager();
    $('#child-dialog').showModal(); if(child && manager()) await loadHistory(id);
  }
  async function loadHistory(id) {
    try { const entries = await api(`/children/${id}/attendance`); $('#history-list').innerHTML = entries.length ? entries.map(a => `<div class="history-row"><span>${date(a.day)}</span><button class="button small-button" type="button" data-history-undo="${a.id}">Entfernen</button></div>`).join('') : '<p class="small muted">Noch keine Anwesenheiten.</p>'; }
    catch(e) { errorBox('#child-error',e.message); }
  }
  function confirmAction(title,text,action) {
    $('#confirm-title').textContent = title; $('#confirm-text').textContent = text;
    $('#confirm-action').onclick = e => busy(e.currentTarget,async()=>{ await action(); $('#confirm-dialog').close(); });
    $('#confirm-dialog').showModal();
  }
  function initChildren() {
    if(page === 'today') $('#today-date').textContent = new Date(session.today + 'T12:00:00Z').toLocaleDateString('de-DE',{weekday:'long',day:'numeric',month:'long',year:'numeric'}).toUpperCase();
    const searchInput=$('#child-search');
    const revealResults=()=>{
      const mobile=matchMedia('(max-width:760px)').matches;
      const active=document.activeElement===searchInput||searchInput.value.trim().length>0;
      $('#main').classList.toggle('searching-children',mobile&&active);
      clearTimeout(searchRevealTimer);
      if(mobile&&document.activeElement===searchInput)searchRevealTimer=setTimeout(()=>$('.search-panel').scrollIntoView({block:'start',behavior:'smooth'}),260);
    };
    searchInput.addEventListener('focus',revealResults);
    searchInput.addEventListener('input',()=>{clearTimeout(searchTimer);searchTimer=setTimeout(()=>{renderChildren();revealResults();},100);});
    searchInput.addEventListener('blur',revealResults);
    $('#include-inactive').addEventListener('change',loadChildren);
    $$('[data-action="new-child"]').forEach(b=>b.addEventListener('click',()=>openChild()));
    $('#children-list').addEventListener('click',e=>{
      const b=e.target.closest('button'); if(!b)return;
      if(b.dataset.edit) openChild(b.dataset.edit);
      if(b.dataset.action==='retry-children') loadChildren();
      if(b.dataset.attend) busy(b,async()=>{const result=await api(`/children/${b.dataset.attend}/attendance`,'POST',{day:session.today});toast(result.alreadyPresent?'Das Kind war bereits erfasst.':'Für heute erfasst.');await loadChildren();});
      if(b.dataset.undo) busy(b,async()=>{await api(`/attendance/${b.dataset.undo}`,'DELETE');toast('Anwesenheit zurückgenommen.');await loadChildren();});
    });
    $('#child-form').addEventListener('submit',async e=>{
      e.preventDefault();const form=e.currentTarget;const button=e.submitter;if(form.dataset.saving==='true')return;form.dataset.saving='true';
      await busy(button,async()=>{
        const id=form.elements.id.value;const payload={siteId,firstName:form.elements.firstName.value,lastName:form.elements.lastName.value,birthDate:form.elements.birthDate.value,gender:form.elements.gender.value,nationalities:[...countrySets['child-countries']],contactName:form.elements.contactName.value,contactPhone:form.elements.contactPhone.value,contactRelationship:form.elements.contactRelationship.value,revision:form.elements.revision.value||null,confirmDuplicate:form.elements.confirmDuplicate.checked};
        try {
          const saved=await api(id?`/children/${id}`:'/children',id?'PUT':'POST',payload);
          // Retain the new ID before a second request, so a connection failure cannot create a second profile.
          form.elements.id.value=saved.id;form.elements.revision.value=saved.revision;
          if(button.value==='attend') {
            try{await api(`/children/${saved.id}/attendance`,'POST',{day:session.today});}
            catch(err){$('#child-dialog').close();await loadChildren();toast('Profil gespeichert; Anwesenheit noch nicht bestätigt. Bitte in der Liste prüfen. '+err.message,true);return;}
          }
          $('#child-dialog').close();toast(button.value==='attend'?'Kind angelegt und heute erfasst.':'Kinderdaten gespeichert.');await loadChildren();
        } catch(err) {errorBox('#child-error',err.message); if(err.status===409 && err.message.includes('existiert'))$('#duplicate-confirm').hidden=false;}
      });
      form.dataset.saving='false';
    });
    $('#history-list').addEventListener('click',e=>{const b=e.target.closest('[data-history-undo]');if(!b)return;busy(b,async()=>{await api(`/attendance/${b.dataset.historyUndo}`,'DELETE');await loadHistory($('#child-form').elements.id.value);await loadChildren();toast('Anwesenheit entfernt.');});});
    $('[data-action="backfill"]').addEventListener('click',e=>busy(e.currentTarget,async()=>{const f=$('#child-form');await api(`/children/${f.elements.id.value}/attendance`,'POST',{day:f.elements.historyDay.value});await loadHistory(f.elements.id.value);await loadChildren();f.elements.revision.value=children.find(c=>c.id===f.elements.id.value)?.revision||f.elements.revision.value;toast('Anwesenheit gespeichert.');}));
    for(const preserve of [false,true]) $(`[data-action="${preserve?'delete-preserve':'delete-invalid'}"]`).addEventListener('click',()=>{
      const id=$('#child-form').elements.id.value;
      confirmAction(preserve?'Personendaten endgültig löschen?':'Fehlanlage endgültig entfernen?',preserve?'Profil, Notfallkontakt und zuordenbare Anwesenheiten werden gelöscht. Nur anonyme Monatssummen bleiben. Diese Aktion kann nicht rückgängig gemacht werden.':'Das Profil und sämtliche zugehörigen Besuche werden auch aus den Auswertungen entfernt. Diese Aktion kann nicht rückgängig gemacht werden.',async()=>{await api(`/children/${id}?preserve=${preserve}`,'DELETE');$('#child-dialog').close();await loadChildren();toast('Profil entfernt.');});
    });
    loadChildren();
  }

  function periodRange(preset) {
    const today=session.today;let start=today;
    if(preset==='week')start=addDays(today,-((weekday(today)+6)%7));
    if(preset==='month')start=today.slice(0,8)+'01';
    if(preset==='year')start=today.slice(0,4)+'-01-01';
    return {start,end:today,preset};
  }
  function baseFilter() {return {...periodRange('week'),siteIds:[siteId],genders:[],nationalities:[],minAge:null,maxAge:null,weekday:null,grouping:'day',compare:true,compareStart:null,compareEnd:null};}
  function renderChips() {
    const chips=[{label:`${date(filter.start)} – ${date(filter.end)}`,key:'dates'},{label:filter.siteIds.map(id=>session.sites.find(s=>s.siteId===id)?.name).join(' + '),key:'sites'}];
    if(filter.minAge!==null||filter.maxAge!==null)chips.push({label:`${filter.minAge??0}–${filter.maxAge??100} Jahre`,key:'age'});
    for(const gender of filter.genders)chips.push({label:({Female:'Weiblich',Male:'Männlich',Diverse:'Divers',Unspecified:'Keine Angabe'})[gender],key:'gender:'+gender});
    for(const code of filter.nationalities)chips.push({label:countryName(code),key:'nationality:'+code});
    if(filter.weekday!==null)chips.push({label:['Sonntag','Montag','Dienstag','Mittwoch','Donnerstag','Freitag','Samstag'][filter.weekday],key:'weekday'});
    if(filter.compareStart)chips.push({label:`Vergleich ${date(filter.compareStart)} – ${date(filter.compareEnd)}`,key:'comparison'});
    $('#active-filters').innerHTML=chips.map(c=>`<span class="filter-chip">${esc(c.label)}<button data-remove-filter="${esc(c.key)}" aria-label="Filter ${esc(c.label)} zurücksetzen">×</button></span>`).join('')+'<button class="reset-link" id="clear-all-filters">Alle zurücksetzen</button>';
    const count=chips.length-2;$('#filter-count').hidden=count===0;$('#filter-count').textContent=count;
    $$('[data-period]').forEach(b=>{const active=b.dataset.period===filter.preset;b.classList.toggle('active',active);b.setAttribute('aria-pressed',active);});
  }
  async function loadReport() {
    const version=++queryVersion;renderChips();$('#export-button').disabled=true;$('#dashboard-results').setAttribute('aria-busy','true');errorBox('#report-error','');
    try {const result=await api('/reports','POST',filter);if(version!==queryVersion)return;report=result;renderReport();$('#export-button').disabled=false;}
    catch(e){if(version!==queryVersion)return;report=null;errorBox('#report-error',e.message);$('#dashboard-results').hidden=true;}
    finally{if(version===queryVersion)$('#dashboard-results').setAttribute('aria-busy','false');}
  }
  function delta(current,previous) {
    if(current===null)return '<span class="unavailable">Für diese Auswahl nicht vollständig verfügbar</span>';
    if(!report.comparison)return 'Ohne Vergleichszeitraum';
    if(previous===null)return 'Vergleich nicht vollständig verfügbar';
    const difference=current-previous;const signed=`${difference>0?'+':''}${number(difference)}`;
    return `<b>${signed}${previous===0?'':` (${difference>0?'+':''}${number(difference/previous*100)} %)`}</b> zum Vergleich${previous===0?' · Ausgangswert 0':''}`;
  }
  function renderReport() {
    $('#dashboard-results').hidden=false;const cur=report.current,prev=report.comparison;
    for(const key of ['visits','children','average']){$('#metric-'+key).textContent=cur.metrics[key]===null?'—':number(cur.metrics[key]);$('#delta-'+key).innerHTML=delta(cur.metrics[key],prev?.metrics[key]??null);}
    $('#children-metric-label').textContent=report.crossSite?'Kinder · Standortzählungen':'Unterschiedliche Kinder';
    $('#empty-report').hidden=cur.metrics.visits!==0;errorBox('#report-notice',cur.notice || prev?.notice || '');
    $('#chart-period').textContent=`${date(cur.start)} – ${date(cur.end)}${prev?` · Vergleich ${date(prev.start)} – ${date(prev.end)}`:''}`;
    $('.legend-previous').hidden=!prev;
    const max=Math.max(1,...cur.timeline.map(b=>b.value),...(prev?.timeline.map(b=>b.value)||[]));
    $('#timeline-chart').innerHTML=cur.timeline.map((b,i)=>`<button type="button" class="timeline-column" data-time="${b.key}" title="${esc(b.label)}: ${b.value} Besuche${prev?`; Vergleich: ${prev.timeline[i]?.value??0}`:''}" aria-label="${esc(b.label)}: ${b.value} Besuche, Zeitraum filtern"><span class="timeline-value">${b.value}</span><span class="bar-pair"><span class="timeline-bar" style="height:${b.value/max*100}%"></span>${prev?`<span class="timeline-bar previous" style="height:${(prev.timeline[i]?.value??0)/max*100}%"></span>`:''}</span><span class="timeline-label">${esc(b.label)}</span></button>`).join('');
    const dayMax=Math.max(1,...cur.weekdays.map(b=>b.value));$('#weekday-chart').innerHTML=cur.weekdays.map(b=>`<button type="button" class="weekday-bar" data-weekday="${b.key}" aria-pressed="${filter.weekday===Number(b.key)}" aria-label="${esc(b.label)}: ${b.value} Besuche, filtern"><strong>${b.value}</strong><i style="height:${b.value/dayMax*135}px"></i><span>${esc(b.label)}</span></button>`).join('');
    horizontal('#age-chart',cur.ages,'age',b=>`${filter.minAge}:${filter.maxAge}`===b.key);
    horizontal('#gender-chart',cur.genders,'gender',b=>filter.genders.includes(b.key));
    horizontal('#nationality-chart',cur.nationalities,'nationality',b=>filter.nationalities.includes(b.key));
    $('#site-chart-panel').hidden=session.sites.length<2;
    horizontal('#site-chart',cur.sites,'site',b=>filter.siteIds.length===1&&filter.siteIds[0]===Number(b.key));
    $('#archive-panel').hidden=!cur.hasArchive;
    $('#archive-table').innerHTML=`<div class="table-scroll"><table><thead><tr><th>Standort</th><th>Monat</th><th>Besuche</th><th>Kinder im Monat</th></tr></thead><tbody>${cur.archive.map(a=>`<tr><td>${esc(a.site)}</td><td>${a.month.slice(5,7)}.${a.month.slice(0,4)}</td><td>${a.visits}</td><td>${a.profiles}</td></tr>`).join('')}</tbody></table></div>`;
  }
  function horizontal(selector,buckets,type,active) {
    const max=Math.max(1,...buckets.map(b=>b.value));$(selector).innerHTML=buckets.length?buckets.map(b=>`<button class="horizontal-row" type="button" data-chart="${type}" data-key="${esc(b.key)}" aria-pressed="${active(b)}" aria-label="${esc(b.label)}: ${b.value} Besuche, filtern"><span>${esc(b.label)}</span><span class="track"><span class="fill" style="width:${b.value/max*100}%"></span></span><strong>${b.value}</strong></button>`).join(''):'<p class="muted small">Keine Besuche in dieser Auswahl.</p>';
  }
  function openFilters() {
    const f=$('#filter-form');f.reset();
    for(const key of ['start','end','minAge','maxAge','weekday','compareStart','compareEnd'])f.elements[key].value=filter[key]??'';
    $$('input[type=date]',f).forEach(i=>i.max=session.today);
    $$('input[name=gender]',f).forEach(i=>i.checked=filter.genders.includes(i.value));
    $('#filter-sites').innerHTML=session.sites.map(s=>`<label class="check"><input name="site" type="checkbox" value="${s.siteId}" ${filter.siteIds.includes(s.siteId)?'checked':''} /> ${esc(s.name)}</label>`).join('');
    f.elements.comparison.value=!filter.compare?'none':filter.compareStart?'custom':'previous';const custom=f.elements.comparison.value==='custom';$('#comparison-dates').hidden=!custom;
    for(const name of ['compareStart','compareEnd'])f.elements[name].required=custom;
    countryPicker('filter-countries',filter.nationalities);$('#filter-dialog').showModal();
  }
  function initDashboard() {
    filter=baseFilter();$('#open-filters').addEventListener('click',openFilters);
    $$('[data-period]').forEach(b=>b.addEventListener('click',()=>{timeSelection=null;Object.assign(filter,periodRange(b.dataset.period),{compareStart:null,compareEnd:null});loadReport();}));
    $('#chart-grouping').addEventListener('change',e=>{filter.grouping=e.target.value;loadReport();});
    $('#active-filters').addEventListener('click',e=>{
      const b=e.target.closest('button');if(!b)return;
      if(b.id==='clear-all-filters'){filter=baseFilter();timeSelection=null;}
      const key=b.dataset.removeFilter;
      if(key==='dates'){Object.assign(filter,timeSelection?.previous||periodRange('week'));timeSelection=null;}
      if(key==='sites')filter.siteIds=[siteId];
      if(key==='age'){filter.minAge=null;filter.maxAge=null;}
      if(key==='weekday')filter.weekday=null;
      if(key==='comparison'){filter.compareStart=null;filter.compareEnd=null;}
      if(key?.startsWith('gender:'))filter.genders=filter.genders.filter(v=>v!==key.split(':')[1]);
      if(key?.startsWith('nationality:'))filter.nationalities=filter.nationalities.filter(v=>v!==key.split(':')[1]);
      $('#chart-grouping').value=filter.grouping;loadReport();
    });
    $('#dashboard-results').addEventListener('click',e=>{
      const b=e.target.closest('button');if(!b)return;
      if(b.dataset.weekday!==undefined){const n=Number(b.dataset.weekday);filter.weekday=filter.weekday===n?null:n;}
      else if(b.dataset.chart==='age'){const [min,max]=b.dataset.key.split(':').map(Number);const reset=filter.minAge===min&&filter.maxAge===max;filter.minAge=reset?null:min;filter.maxAge=reset?null:max;}
      else if(b.dataset.chart==='gender'){const key=b.dataset.key;filter.genders=filter.genders.includes(key)?filter.genders.filter(x=>x!==key):[...filter.genders,key];}
      else if(b.dataset.chart==='nationality'){const key=b.dataset.key;filter.nationalities=filter.nationalities.includes(key)?filter.nationalities.filter(x=>x!==key):[...filter.nationalities,key];}
      else if(b.dataset.chart==='site'){const key=Number(b.dataset.key);filter.siteIds=filter.siteIds.length===1&&filter.siteIds[0]===key?session.sites.map(s=>s.siteId):[key];}
      else if(b.dataset.time){
        if(timeSelection?.key===b.dataset.time){Object.assign(filter,timeSelection.previous);timeSelection=null;}
        else {const previous={start:filter.start,end:filter.end,preset:filter.preset};let start=b.dataset.time,end=start;if(filter.grouping==='week')end=addDays(start,6);if(filter.grouping==='month'){const d=new Date(start+'T12:00:00Z');d.setUTCMonth(d.getUTCMonth()+1);d.setUTCDate(0);end=iso(d);}filter.start=start<previous.start?previous.start:start;filter.end=end>previous.end?previous.end:end;filter.preset='custom';timeSelection={key:b.dataset.time,previous};}
      }
      else return;
      loadReport();
    });
    $('#filter-form').elements.comparison.addEventListener('change',e=>{const custom=e.target.value==='custom';$('#comparison-dates').hidden=!custom;for(const name of ['compareStart','compareEnd'])$('#filter-form').elements[name].required=custom;});
    $('#reset-filters').addEventListener('click',()=>{filter=baseFilter();timeSelection=null;$('#filter-dialog').close();$('#chart-grouping').value='day';loadReport();});
    $('#filter-form').addEventListener('submit',e=>{
      e.preventDefault();const f=e.currentTarget;const get=n=>f.elements[n].value;const sites=$$('input[name=site]:checked',f).map(i=>Number(i.value));
      if(!sites.length){toast('Bitte mindestens einen Standort auswählen.',true);return;}
      const datesChanged=get('start')!==filter.start||get('end')!==filter.end;
      if(datesChanged)timeSelection=null;
      Object.assign(filter,{start:get('start'),end:get('end'),preset:datesChanged?'custom':filter.preset,siteIds:sites,minAge:get('minAge')===''?null:Number(get('minAge')),maxAge:get('maxAge')===''?null:Number(get('maxAge')),genders:$$('input[name=gender]:checked',f).map(i=>i.value),nationalities:[...countrySets['filter-countries']],weekday:get('weekday')===''?null:Number(get('weekday')),compare:get('comparison')!=='none',compareStart:get('comparison')==='custom'?get('compareStart'):null,compareEnd:get('comparison')==='custom'?get('compareEnd'):null});
      $('#filter-dialog').close();loadReport();
    });
    $('#export-button').addEventListener('click',e=>busy(e.currentTarget,async()=>{if(!report)return;const blob=await api('/reports/export','POST',report.filter,true);const url=URL.createObjectURL(blob);const link=document.createElement('a');link.href=url;link.download=`NeuerKids-Auswertung-${report.filter.start}-${report.filter.end}.xlsx`;link.click();setTimeout(()=>URL.revokeObjectURL(url),1000);toast('Excel-Auswertung heruntergeladen.');}));
    loadReport();
  }

  let accounts=[];
  async function loadUsers() {
    try{accounts=await api('/admin/users');$('#users-list').innerHTML=accounts.map(u=>`<article class="child-row"><div class="child-avatar">${esc(u.displayName.slice(0,2).toUpperCase())}</div><div class="child-main"><button data-user="${esc(u.id)}">${esc(u.displayName)}</button><div class="child-meta">${esc(u.email)} ${u.isBlocked?'<span class="archive-badge">Gesperrt</span>':''}</div><div class="child-meta">${u.isAdmin?'Zentrale Administration · ':''}${u.sites.map(s=>`${s.siteId===1?'Gelsenkirchen':'Bottrop'}: ${s.role==='Manager'?'Hausleitung':'Mitarbeitende'}`).join(' · ')||(!u.isAdmin?'Kein Standortzugriff':'')}</div></div><button class="button small-button" data-user="${esc(u.id)}">Verwalten</button></article>`).join('');}
    catch(e){errorBox('#admin-error',e.message);}
  }
  function openUser(id) {
    const u=accounts.find(u=>u.id===id),f=$('#user-form');f.reset();f.elements.id.value=id||'';
    f.elements.name.value=u?.displayName||'';f.elements.email.value=u?.email||'';f.elements.name.disabled=!!u;f.elements.email.disabled=!!u;
    f.elements.isAdmin.checked=u?.isAdmin||false;f.elements.isBlocked.checked=u?.isBlocked||false;
    for(const site of [1,2])f.elements['site'+site].value=u?.sites.find(s=>s.siteId===site)?.role||'';
    $('#user-dialog-title').textContent=u?'Zugang verwalten':'Mitarbeitende einladen';$('#blocked-control').hidden=!u;$('#reset-controls').hidden=!u;errorBox('#user-error','');$('#user-dialog').showModal();
  }
  function showActivation(path) {$('#user-dialog').close();$('#activation-link').value=location.origin+path;$('#activation-dialog').showModal();}
  function initAdmin() {
    if(!session.isAdmin){$('#invite-button').hidden=true;errorBox('#admin-error','Dieser Bereich ist der zentralen Administration vorbehalten.');return;}
    $('#invite-button').addEventListener('click',()=>openUser());
    $('#users-list').addEventListener('click',e=>{const b=e.target.closest('[data-user]');if(b)openUser(b.dataset.user);});
    $('#user-form').addEventListener('submit',e=>{e.preventDefault();const f=e.currentTarget;busy(e.submitter,async()=>{const sites=[1,2].filter(id=>f.elements['site'+id].value).map(id=>({siteId:id,role:f.elements['site'+id].value}));const payload={name:f.elements.name.value,email:f.elements.email.value,isAdmin:f.elements.isAdmin.checked,isBlocked:f.elements.isBlocked.checked,sites};try{const result=await api('/admin/users'+(f.elements.id.value?'/'+f.elements.id.value:''),f.elements.id.value?'PUT':'POST',payload);if(result?.activationPath)showActivation(result.activationPath);else{$('#user-dialog').close();toast('Zugangsrechte gespeichert. Bestehende Sitzungen wurden beendet.');}await loadUsers();}catch(err){errorBox('#user-error',err.message);}});});
    $('#copy-activation').addEventListener('click',e=>busy(e.currentTarget,async()=>{await navigator.clipboard.writeText($('#activation-link').value);toast('Aktivierungslink kopiert.');}));
    for(const reset of [false,true])$(`[data-action="${reset?'reset-mfa':'reset-password'}"]`).addEventListener('click',e=>busy(e.currentTarget,async()=>{const result=await api(`/admin/users/${$('#user-form').elements.id.value}/reset`,'POST',{resetAuthenticator:reset});showActivation(result.activationPath);await loadUsers();}));
    $('#activation-dialog').addEventListener('close',()=>$('#activation-link').value='');
    loadUsers();
  }
  async function init() {
    if(page==='account'||!$('#site-picker'))return;
    try{
      session=await api('/session');const preferred=Number(new URLSearchParams(location.search).get('site'));const reportSites=session.sites.filter(s=>s.role==='Manager');const pageSites=page==='dashboard'?reportSites:session.sites;siteId=pageSites.find(s=>s.siteId===preferred)?.siteId||pageSites[0]?.siteId;
      $('#user-name').textContent=session.displayName;$('#admin-nav').hidden=!session.isAdmin;$('#dashboard-nav').hidden=!reportSites.length;
      $('#site-picker').innerHTML=pageSites.length?pageSites.map(s=>`<option value="${s.siteId}">${esc(s.name)}</option>`).join(''):'<option>Administration</option>';$('#site-picker').value=siteId||'';$('#site-picker').disabled=pageSites.length<2;
      $('#site-picker').addEventListener('change',e=>{siteId=Number(e.target.value);setNav();if(page==='dashboard'){filter.siteIds=[siteId];loadReport();}if(page==='today'||page==='children')loadChildren();});
      setNav();
      if(page==='dashboard'&&!reportSites.length){$('#main').innerHTML='<div class="empty-state"><h1>Keine Auswertungsrechte.</h1><p>Auswertungen stehen der Hausleitung zur Verfügung.</p><a href="/" class="button primary">Zur Tagesansicht</a></div>';return;}
      if(!session.sites.length&&page!=='admin'){$('#main').innerHTML='<div class="empty-state"><h1>Willkommen.</h1><p>Deinem Zugang ist noch kein Standort zugewiesen.</p>'+(session.isAdmin?'<a href="/Admin" class="button primary">Zugänge verwalten</a>':'<p>Bitte wende dich an die zentrale Administration.</p>')+'</div>';return;}
      if(page==='today'||page==='children')initChildren();if(page==='dashboard')initDashboard();if(page==='admin')initAdmin();
    }catch(e){toast(e.message,true);}
  }
  init();
})();
