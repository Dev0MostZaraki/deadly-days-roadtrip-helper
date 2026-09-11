function baseItemScore(it,ctx){
  let s=it.base;
  const ch=CHARACTERS[ctx.character], gw=GOAL_WEIGHTS[ctx.goal]||{}, sm=STAGE_MOD[ctx.stage]||{};
  for(const t of it.tags){s+=(ch.weights[t]||0);s+=(gw[t]||0);s+=(sm[t]||0)}
  if(ch.signature===it.id) s+=7;
  // Large items must earn their space; signatures get exempted from most of this penalty.
  const sizePenalty=(ch.signature===it.id?0.08:0.22)*Math.max(0,area(it)-1);
  s-=sizePenalty;
  if(it.id==='void') s+=ctx.freeSpacePotential?1:0;
  return s;
}

function placementsFor(it,bag){
  const bagKeys=bag, res=[];
  for(const shape of rotations(it)){
    for(let oy=0;oy<GRID;oy++) for(let ox=0;ox<GRID;ox++){
      const cells=translate(shape,ox,oy);
      if(cells.every(([x,y])=>x>=0&&y>=0&&x<GRID&&y<GRID&&bagKeys.has(key(x,y)))) res.push({cells,shape,ox,oy});
    }
  }
  const seen=new Set(); return res.filter(p=>{const id=p.cells.map(([x,y])=>key(x,y)).sort().join('|');if(seen.has(id))return false;seen.add(id);return true});
}
function markerCells(it,placement){
  if(it.exactMarker){
    // exactMarker is stored relative to the normalized, unrotated item. Rotation-aware masks will be added as field data becomes verified.
    return new Set(it.exactMarker.map(([x,y])=>key(x+placement.ox,y+placement.oy)));
  }
  return around8(placement.cells);
}
function evaluateLayout(instances,placements,bag,ctx){
  let score=0; const synergies=[], warnings=[]; const placed=instances.filter(i=>placements[i.iid]);
  for(const inst of placed) score+=baseItemScore(instanceItem(inst),ctx);
  for(const sInst of placed){
    const sItem=instanceItem(sInst); if(!sItem.support) continue;
    const zone=markerCells(sItem,placements[sInst.iid]);
    for(const tInst of placed){
      if(tInst.iid===sInst.iid) continue; const tItem=instanceItem(tInst);
      if(!isTarget(sItem,tItem)) continue;
      const hit=placements[tInst.iid].cells.some(([x,y])=>zone.has(key(x,y)));
      if(hit){
        let v=sItem.synergy;
        if(CHARACTERS[ctx.character].signature===tItem.id) v+=1.5;
        if(tItem.tags.includes('triggerable') && ['running_shoes','rollerblades','toolbelt'].includes(sItem.id)) v+=1.5;
        if(tItem.tags.includes('inflatable') && sItem.id==='pump') v+=1;
        score+=v;
        synergies.push({support:sItem.name,target:tItem.name,value:v,approx:!sItem.exactMarker});
      }
    }
  }
  const occupied=new Set(); Object.values(placements).forEach(p=>p.cells.forEach(([x,y])=>occupied.add(key(x,y))));
  const free=bag.size-occupied.size;
  const freeWeight=ctx.stage==='early'?.22:ctx.stage==='early-mid'?.16:ctx.stage==='mid'?.09:.04;
  score+=free*freeWeight;
  const hasApprox=placed.some(i=>instanceItem(i).support&&!instanceItem(i).exactMarker);
  if(hasApprox) warnings.push('Support-Reichweiten sind teilweise noch nicht feldgenau vermessen. Bis dahin nutzt der Optimierer eine sichtbare 8-Nachbar-Näherung.');
  const low=placed.filter(i=>instanceItem(i).confidence==='low').map(i=>instanceItem(i).name);
  if(low.length) warnings.push(`Niedrige Daten-Confidence: ${uniq(low).join(', ')}.`);
  return {score,synergies,warnings,free,occupied};
}

function optimizeLayout(instances,bag,ctx,{allowDrop=false,mustInclude=null,beamWidth=900}={}){
  if(!bag.size) return {ok:false,score:-Infinity,placements:{},placed:[],dropped:instances,warnings:['Der Rucksack hat keine aktiven Felder.']};
  const sorted=[...instances].sort((a,b)=>{
    const aa=area(instanceItem(a)),ab=area(instanceItem(b));
    const pa=(CHARACTERS[ctx.character].signature===a.itemId?20:0)+(a.iid===mustInclude?50:0);
    const pb=(CHARACTERS[ctx.character].signature===b.itemId?20:0)+(b.iid===mustInclude?50:0);
    return (pb+ab*2+instanceItem(b).base)-(pa+aa*2+instanceItem(a).base);
  });
  let beam=[{occ:new Set(),placements:{},placed:[],dropped:[],partial:0}];
  for(const inst of sorted){
    const it=instanceItem(inst), poss=placementsFor(it,bag); const next=[];
    for(const st of beam){
      for(const p of poss){
        if(p.cells.some(([x,y])=>st.occ.has(key(x,y)))) continue;
        const occ=new Set(st.occ); p.cells.forEach(([x,y])=>occ.add(key(x,y)));
        const placements={...st.placements,[inst.iid]:p};
        const placed=[...st.placed,inst];
        const ev=evaluateLayout(placed,placements,bag,ctx);
        next.push({occ,placements,placed,dropped:[...st.dropped],partial:ev.score});
      }
      const protectedItem=CHARACTERS[ctx.character].signature===it.id || inst.iid===mustInclude;
      if(allowDrop && !protectedItem){
        const penalty=Math.max(1,baseItemScore(it,ctx)*.45);
        next.push({...st,dropped:[...st.dropped,inst],partial:st.partial-penalty});
      }
    }
    if(!next.length){
      if(inst.iid===mustInclude || CHARACTERS[ctx.character].signature===it.id) return {ok:false,score:-Infinity,placements:{},placed:[],dropped:instances,warnings:[`${it.name} passt in keiner zulässigen Anordnung in die aktuelle Form.`]};
      continue;
    }
    next.sort((a,b)=>b.partial-a.partial);
    beam=next.slice(0,beamWidth);
  }
  let best=null;
  for(const st of beam){
    if(mustInclude && !st.placed.some(i=>i.iid===mustInclude)) continue;
    const ev=evaluateLayout(st.placed,st.placements,bag,ctx);
    // Additional penalty for dropped items so replacement cost remains visible.
    const dropPenalty=st.dropped.reduce((n,i)=>n+Math.max(0,baseItemScore(instanceItem(i),ctx)*.25),0);
    const finalScore=ev.score-dropPenalty;
    if(!best||finalScore>best.score) best={ok:true,score:finalScore,placements:st.placements,placed:st.placed,dropped:st.dropped,...ev,score:finalScore};
  }
  return best||{ok:false,score:-Infinity,placements:{},placed:[],dropped:instances,warnings:['Keine vollständige Anordnung gefunden.']};
}

function expansionPlacements(expansion,bag){
  const res=[];
  for(const shape of rotations(expansion)){
    for(let oy=0;oy<GRID;oy++) for(let ox=0;ox<GRID;ox++){
      const cells=translate(shape,ox,oy);
      if(cells.some(([x,y])=>x<0||y<0||x>=GRID||y>=GRID||bag.has(key(x,y)))) continue;
      if(!cells.some(c=>orthNeighbors(c).some(([nx,ny])=>bag.has(key(nx,ny))))) continue;
      const newBag=new Set(bag); cells.forEach(([x,y])=>newBag.add(key(x,y)));
      res.push({cells,newBag});
    }
  }
  return res;
}
function compactness(bag){
  const cells=[...bag].map(parseKey); if(!cells.length)return 0;
  const minX=Math.min(...cells.map(c=>c[0])),maxX=Math.max(...cells.map(c=>c[0])),minY=Math.min(...cells.map(c=>c[1])),maxY=Math.max(...cells.map(c=>c[1]));
  const box=(maxX-minX+1)*(maxY-minY+1); return bag.size/box;
}
function evaluateExpansion(exp,ctx,baseline){
  const ps=expansionPlacements(exp,state.bag); let best=null;
  for(const p of ps){
    const r=optimizeLayout(state.items,p.newBag,ctx,{allowDrop:false,beamWidth:600}); if(!r.ok)continue;
    const stageSpace=ctx.stage==='early'?1.4:ctx.stage==='early-mid'?1.15:ctx.stage==='mid'?.85:.55;
    const future=exp.shape.length*stageSpace + compactness(p.newBag)*1.5;
    const score=r.score+future;
    if(!best||score>best.total) best={total:score,layout:r,newBag:p.newBag,added:p.cells,future};
  }
  if(!best) return {ok:false,total:-Infinity,delta:-Infinity,reason:'Die Erweiterung kann im 6×6-Editor aktuell nicht sinnvoll angesetzt werden.'};
  return {ok:true,...best,delta:best.total-baseline.score};
}
function evaluateItemCandidate(itemId,ctx,baseline){
  const iid=`cand-${Date.now()}-${Math.random()}`; const inst={iid,itemId}; const list=[...state.items,inst];
  const r=optimizeLayout(list,state.bag,ctx,{allowDrop:true,mustInclude:iid,beamWidth:1100});
  if(!r.ok)return {ok:false,total:-Infinity,delta:-Infinity,reason:r.warnings?.[0]||'Passt nicht.'};
  return {ok:true,total:r.score,delta:r.score-baseline.score,layout:r,candidateInstance:inst};
}
