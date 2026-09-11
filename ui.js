function context(){return {character:state.character,stage:state.stage,goal:state.goal,freeSpacePotential:true}}
function newInstance(itemId){return {iid:`i${state.nextInstance++}`,itemId}}
function addItem(itemId){state.items.push(newInstance(itemId));save();renderItems();renderOptimized()}
function removeInstance(iid){state.items=state.items.filter(i=>i.iid!==iid);save();renderItems();renderOptimized()}
function defaultBag(){
  // User's current 12-slot shape: two indented 4-cell rows over one 4-cell row.
  return new Set([[1,1],[2,1],[3,1],[4,1],[1,2],[2,2],[3,2],[4,2],[0,3],[1,3],[2,3],[3,3]].map(([x,y])=>key(x,y)));
}
function loadPresidentPreset(){state.character='president';state.stage='early-mid';state.goal='balanced';state.bag=defaultBag();state.items=[];state.nextInstance=1;['knabe_kola','pistol','grenade','firework','pump'].forEach(add=>state.items.push(newInstance(add)));save();renderAll()}
function reset(){state.character='president';state.stage='early-mid';state.goal='balanced';state.bag=new Set();state.items=[];state.nextInstance=1;save();renderAll()}

function save(){
  const payload={character:state.character,stage:state.stage,goal:state.goal,bag:[...state.bag],items:state.items,nextInstance:state.nextInstance};
  localStorage.setItem('ddrt-helper-run',JSON.stringify(payload));
}
function load(){
  try{const p=JSON.parse(localStorage.getItem('ddrt-helper-run'));if(!p)return false;state.character=p.character||'president';state.stage=p.stage||'early-mid';state.goal=p.goal||'balanced';state.bag=new Set(p.bag||[]);state.items=(p.items||[]).filter(i=>ITEMS[i.itemId]);state.nextInstance=p.nextInstance||1;return true}catch{return false}
}
function runCode(){return btoa(unescape(encodeURIComponent(JSON.stringify({v:2,patch:PATCH,character:state.character,stage:state.stage,goal:state.goal,bag:[...state.bag],items:state.items}))))}
function importCode(code){
  const p=JSON.parse(decodeURIComponent(escape(atob(code.trim())))); state.character=p.character;state.stage=p.stage;state.goal=p.goal;state.bag=new Set(p.bag);state.items=p.items.filter(i=>ITEMS[i.itemId]);state.nextInstance=Math.max(1,...state.items.map(i=>Number(String(i.iid).replace(/\D/g,''))||0))+1;save();renderAll();
}

function renderSelectors(){
  patchBadge.textContent=PATCH;
  characterSelect.innerHTML=Object.values(CHARACTERS).map(c=>`<option value="${c.id}">${c.name}</option>`).join('');characterSelect.value=state.character;
  stageSelect.value=state.stage;goalSelect.value=state.goal;
  const sorted=Object.values(ITEMS).sort((a,b)=>a.name.localeCompare(b.name,'de'));
  addItemSelect.innerHTML=sorted.map(i=>`<option value="${i.id}">${i.name}</option>`).join('');
  const candidates=[...sorted,...Object.values(EXPANSIONS)];
  document.querySelectorAll('.candidate').forEach((s,idx)=>{s.innerHTML=candidates.map(i=>`<option value="${i.id}">${i.name}</option>`).join('');});
  const defaults=['running_shoes','toolbelt','bag4'];document.querySelectorAll('.candidate').forEach((s,i)=>s.value=defaults[i]);
}
function renderCharacterInfo(){
  const c=CHARACTERS[state.character]; const sig=c.signature?ITEMS[c.signature]:null;
  characterInfo.innerHTML=`<b>${c.focus}</b><br>${c.info}${sig?`<br><span class="muted">Signatur geschützt: ${sig.name}</span>`:''}`;
}
function renderItems(){
  const counts={}; state.items.forEach(i=>counts[i.itemId]=(counts[i.itemId]||0)+1);
  currentItems.innerHTML=state.items.length?state.items.map(inst=>{const it=instanceItem(inst),prot=CHARACTERS[state.character].signature===it.id;return `<span class="chip ${prot?'protected':''}" title="${it.description.replaceAll('"','&quot;')}">${it.name}${counts[it.id]>1?` <span class="tier">#${state.items.filter(x=>x.itemId===it.id).indexOf(inst)+1}</span>`:''}${prot?' ★':''}<button data-remove="${inst.iid}" aria-label="Entfernen">×</button></span>`}).join(''):'<span class="muted">Noch keine Items eingetragen.</span>';
  currentItems.querySelectorAll('[data-remove]').forEach(b=>b.onclick=()=>removeInstance(b.dataset.remove));
}
function renderBagEditor(){
  bagEditor.innerHTML=''; for(let y=0;y<GRID;y++)for(let x=0;x<GRID;x++){const d=document.createElement('button');d.className='bag-cell'+(state.bag.has(key(x,y))?' active':'');d.title=`Feld ${x+1}/${y+1}`;d.onclick=()=>{const k=key(x,y);state.bag.has(k)?state.bag.delete(k):state.bag.add(k);save();renderBagEditor();renderOptimized()};bagEditor.appendChild(d)} bagCount.textContent=`${state.bag.size} Rucksackfelder aktiv`;
}
function normalizeBag(){
  if(!state.bag.size)return; const cells=[...state.bag].map(parseKey),minX=Math.min(...cells.map(c=>c[0])),minY=Math.min(...cells.map(c=>c[1]));state.bag=new Set(cells.map(([x,y])=>key(x-minX,y-minY)));save();renderBagEditor();renderOptimized();
}
function cellClass(it){if(it.tags.includes('signature'))return 'signature';if(it.tags.includes('support'))return 'support';if(it.tags.includes('throwable'))return 'throwable';if(it.tags.includes('weapon'))return 'weapon';if(it.tags.includes('defense'))return 'defense';return ''}
function shortName(n){return n.split(/\s+/).map(w=>w[0]).join('').slice(0,4).toUpperCase()}
function renderLayout(result,bag=state.bag){
  layoutGrid.innerHTML=''; const owner={}; if(result?.placements) for(const inst of result.placed||state.items){const p=result.placements[inst.iid];if(p)p.cells.forEach(([x,y])=>owner[key(x,y)]=inst)}
  for(let y=0;y<GRID;y++)for(let x=0;x<GRID;x++){const k=key(x,y),d=document.createElement('div'),inst=owner[k];d.className='layout-cell'+(bag.has(k)?' bag':'');if(inst){const it=instanceItem(inst);d.className+=' occupied '+cellClass(it);d.innerHTML=`<span class="cell-label">${shortName(it.name)}</span>`;d.title=`${it.name}: ${it.description}`;} layoutGrid.appendChild(d)}
  if(!result?.ok){layoutScore.innerHTML='<span>Keine gültige Anordnung</span>';synergyList.innerHTML='';layoutWarnings.innerHTML=(result?.warnings||[]).map(w=>`<div class="warning">${w}</div>`).join('');return}
  layoutScore.innerHTML=`<strong>${result.score.toFixed(1)}</strong><span>Build-/Layout-Score · ${result.free} freie Felder</span>`;
  synergyList.innerHTML=result.synergies.length?result.synergies.sort((a,b)=>b.value-a.value).map(s=>`<div class="synergy ${s.approx?'approx':''}"><b>${s.support}</b> → ${s.target} <span class="muted">+${s.value.toFixed(1)}${s.approx?' · Reichweite vorläufig':''}</span></div>`).join(''):'<div class="muted">Keine aktive Support-Synergie in der besten gefundenen Anordnung.</div>';
  const drop=result.dropped?.length?`<div class="warning">Nicht untergebracht: ${result.dropped.map(i=>instanceItem(i).name).join(', ')}</div>`:'';
  layoutWarnings.innerHTML=drop+(result.warnings||[]).map(w=>`<div class="warning">${w}</div>`).join('');
}
function renderOptimized(){
  const r=optimizeLayout(state.items,state.bag,context(),{allowDrop:false,beamWidth:1000});renderLayout(r);return r;
}
function explainCandidate(id,res,baseline){
  if(!res.ok)return res.reason||'Keine gültige Lösung gefunden.';
  if(EXPANSIONS[id]){
    const n=EXPANSIONS[id].shape.length; return `${n} neue Felder lösen Platzengpässe und erhöhen den Optionswert. Die beste Ansetzposition wird gegen den aktuellen Rucksack getestet. ${res.layout.free} Felder bleiben danach frei.`;
  }
  const it=ITEMS[id], dropped=res.layout.dropped||[]; const bits=[];
  if(CHARACTERS[state.character].signature===id)bits.push('Signaturitem dieses Charakters');
  const ch=CHARACTERS[state.character]; const fit=it.tags.reduce((s,t)=>s+(ch.weights[t]||0),0); if(fit>=3)bits.push('sehr hoher Charakter-Fit');else if(fit>=1.5)bits.push('guter Charakter-Fit');
  const syn=res.layout.synergies.filter(s=>s.support===it.name||s.target===it.name); if(syn.length)bits.push(`${syn.length} aktive neue/erhaltene Synergien`);
  if(dropped.length)bits.push(`verdrängt: ${dropped.map(i=>instanceItem(i).name).join(', ')}`);else bits.push('kein bestehendes Item muss raus');
  return bits.join(' · ')||it.description;
}
function recommend(){
  const ctx=context(),baseline=optimizeLayout(state.items,state.bag,ctx,{allowDrop:false,beamWidth:1000});
  if(!baseline.ok){recommendations.innerHTML='<div class="warning">Zuerst muss der aktuelle Rucksack gültig angeordnet werden.</div>';return}
  const ids=[...document.querySelectorAll('.candidate')].map(s=>s.value);
  const results=ids.map(id=>({id,res:EXPANSIONS[id]?evaluateExpansion(EXPANSIONS[id],ctx,baseline):evaluateItemCandidate(id,ctx,baseline)}));
  const finite=results.filter(x=>Number.isFinite(x.res.delta)); const deltas=finite.map(x=>x.res.delta),min=Math.min(...deltas,0),max=Math.max(...deltas,1);
  results.forEach(x=>{x.rating=x.res.ok?clamp(50+((x.res.delta-min)/(Math.max(.01,max-min)))*45 + Math.min(5,Math.max(-5,x.res.delta)),1,100):1});
  results.sort((a,b)=>b.res.total-a.res.total);
  recommendations.innerHTML=results.map((x,rank)=>{const name=EXPANSIONS[x.id]?.name||ITEMS[x.id]?.name||x.id;const r=x.res;const best=rank===0&&r.ok;const drop=r.layout?.dropped?.map(i=>instanceItem(i).name).join(', ')||'—';const free=r.layout?.free??'—';return `<article class="rec ${best?'best':''}"><div class="rank">${rank+1}</div><h4>${name}</h4><div class="rec-score">${r.ok?x.rating.toFixed(0):'—'}/100</div><div class="delta ${r.delta>=0?'pos':'neg'}">${r.ok?(r.delta>=0?'+':'')+r.delta.toFixed(1)+' vs. aktuell':'nicht passend'}</div><p>${explainCandidate(x.id,r,baseline)}</p><div class="facts"><div class="fact"><span>Neuer Gesamtwert</span><b>${r.ok?r.total.toFixed(1):'—'}</b></div><div class="fact"><span>Freie Felder</span><b>${free}</b></div><div class="fact"><span>Verdrängt</span><b>${drop}</b></div></div></article>`}).join('');
  // Best result is useful enough to preview its exact next layout immediately.
  const best=results[0]; if(best?.res?.ok){if(EXPANSIONS[best.id])renderLayout(best.res.layout,best.res.newBag);else renderLayout(best.res.layout,state.bag)}
}
function renderCharacters(){
  characterCards.innerHTML=Object.values(CHARACTERS).map(c=>`<article class="char-card"><h3>${c.name}</h3><div class="focus">${c.focus}</div><p>${c.info}</p><div class="tagrow">${Object.entries(c.weights).sort((a,b)=>b[1]-a[1]).slice(0,6).map(([t,v])=>`<span class="tag">${tagLabel(t)} +${v}</span>`).join('')}<span class="tag ${c.confidence}">${c.confidence}</span></div></article>`).join('');
}
function renderDatabase(filter=''){
  const q=filter.trim().toLowerCase(),rows=Object.values(ITEMS).filter(i=>!q||`${i.name} ${i.tags.join(' ')} ${i.description}`.toLowerCase().includes(q)).sort((a,b)=>a.name.localeCompare(b.name,'de'));
  itemTable.innerHTML=`<div class="table-wrap"><table><thead><tr><th>Item</th><th>Form</th><th>Tags</th><th>Effekt / Rolle</th><th>Confidence</th></tr></thead><tbody>${rows.map(i=>`<tr><td><span class="item-name">${i.name}</span>${i.recipe?`<br><span class="muted">Craft: ${i.recipe.map(x=>ITEMS[x]?.name||x).join(' + ')}</span>`:''}</td><td>${i.shape.length} Feld${i.shape.length===1?'':'er'}</td><td>${i.tags.map(t=>`<span class="tag">${tagLabel(t)}</span>`).join(' ')}</td><td>${i.description}${i.notes?`<br><span class="muted">${i.notes}</span>`:''}</td><td><span class="conf ${i.confidence}">${i.confidence}</span></td></tr>`).join('')}</tbody></table></div>`;
}
function renderSources(){sourceList.className='source-list';sourceList.innerHTML=Object.values(SOURCES).map(s=>`<div class="source-card"><b>${s.title}</b><span>${s.note}</span><br><a href="${s.url}" target="_blank" rel="noopener">Quelle öffnen ↗</a></div>`).join('')+`<div class="warning">Wichtig: Exakte Marker-Geometrien der Support-Items werden erst dann als „high confidence“ genutzt, wenn sie anhand eines Ingame-Screenshots verifiziert wurden. Die Engine kann bereits rotation-aware Marker-Masken verarbeiten.</div>`}
function renderAll(){renderSelectors();renderCharacterInfo();renderItems();renderBagEditor();renderOptimized();renderCharacters();renderDatabase(itemSearch?.value||'');renderSources()}

// Events
loadPreset.onclick=loadPresidentPreset;resetRun.onclick=reset;normalizeBag.onclick=normalizeBag;optimize.onclick=renderOptimized;recommend.onclick=recommend;
addItem.onclick=()=>addItem(addItemSelect.value);
characterSelect.onchange=e=>{state.character=e.target.value;save();renderCharacterInfo();renderItems();renderOptimized()};
stageSelect.onchange=e=>{state.stage=e.target.value;save();renderOptimized()};goalSelect.onchange=e=>{state.goal=e.target.value;save();renderOptimized()};
copyRun.onclick=async()=>{const code=runCode();try{await navigator.clipboard.writeText(code);copyRun.textContent='Kopiert ✓';setTimeout(()=>copyRun.textContent='Run-Code kopieren',1200)}catch{prompt('Run-Code kopieren:',code)}};
importRun.onclick=()=>{const c=prompt('Run-Code einfügen:');if(!c)return;try{importCode(c)}catch(err){alert('Run-Code konnte nicht geladen werden.')}};
itemSearch.oninput=e=>renderDatabase(e.target.value);
document.querySelectorAll('.tab').forEach(b=>b.onclick=()=>{document.querySelectorAll('.tab').forEach(x=>x.classList.toggle('active',x===b));document.querySelectorAll('.tab-panel').forEach(p=>p.classList.toggle('active',p.id===b.dataset.tab))});

if(!load())loadPresidentPreset();else renderAll();
