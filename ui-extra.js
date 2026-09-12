// 14x12 live-canvas overrides. Kept separate from the original prototype UI to make the migration explicit.
renderBagEditor = function(){
  bagEditor.innerHTML='';
  bagEditor.style.gridTemplateColumns=`repeat(${GRID_W},28px)`;
  bagEditor.style.gridTemplateRows=`repeat(${GRID_H},28px)`;
  for(let y=0;y<GRID_H;y++)for(let x=0;x<GRID_W;x++){
    const d=document.createElement('button');
    d.className='bag-cell'+(state.bag.has(key(x,y))?' active':'');
    d.title=`Feld ${x+1}/${y+1}`;
    d.onclick=()=>{const k=key(x,y);state.bag.has(k)?state.bag.delete(k):state.bag.add(k);save();renderBagEditor();renderOptimized()};
    bagEditor.appendChild(d);
  }
  bagCount.textContent=`${state.bag.size} Rucksackfelder aktiv`;
};

renderLayout = function(result,bag=state.bag){
  layoutGrid.innerHTML='';
  layoutGrid.style.setProperty('--s','28px');
  layoutGrid.style.gridTemplateColumns=`repeat(${GRID_W},var(--s))`;
  layoutGrid.style.gridTemplateRows=`repeat(${GRID_H},var(--s))`;
  layoutGrid.style.minHeight='0';
  const owner={};
  if(result?.placements) for(const inst of result.placed||state.items){const p=result.placements[inst.iid];if(p)p.cells.forEach(([x,y])=>owner[key(x,y)]=inst)}
  for(let y=0;y<GRID_H;y++)for(let x=0;x<GRID_W;x++){
    const k=key(x,y),d=document.createElement('div'),inst=owner[k];
    d.className='layout-cell'+(bag.has(k)?' bag':'');
    if(inst){const it=instanceItem(inst);d.className+=' occupied '+cellClass(it);d.innerHTML=`<span class="cell-label">${shortName(it.name)}</span>`;d.title=`${it.name}: ${it.description}`;}
    layoutGrid.appendChild(d);
  }
  if(!result?.ok){layoutScore.innerHTML='<span>Keine gültige Anordnung</span>';synergyList.innerHTML='';layoutWarnings.innerHTML=(result?.warnings||[]).map(w=>`<div class="warning">${w}</div>`).join('');return}
  layoutScore.innerHTML=`<strong>${result.score.toFixed(1)}</strong><span>Build-/Layout-Score · ${result.free} freie Felder</span>`;
  synergyList.innerHTML=result.synergies.length?result.synergies.sort((a,b)=>b.value-a.value).map(s=>`<div class="synergy ${s.approx?'approx':''}"><b>${s.support}</b> → ${s.target} <span class="muted">+${s.value.toFixed(1)}${s.approx?' · Reichweite vorläufig':''}</span></div>`).join(''):'<div class="muted">Keine aktive Support-Synergie in der besten gefundenen Anordnung.</div>';
  const drop=result.dropped?.length?`<div class="warning">Nicht untergebracht: ${result.dropped.map(i=>instanceItem(i).name).join(', ')}</div>`:'';
  layoutWarnings.innerHTML=drop+(result.warnings||[]).map(w=>`<div class="warning">${w}</div>`).join('');
};

// Re-render once after the legacy 6x6 prototype has initialized.
renderAll();
