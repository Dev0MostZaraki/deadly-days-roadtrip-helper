const PATCH = '0.22.1';
const GRID = 6;

const SOURCES = {
  steam022: {
    title: 'Offizielle Steam-Patchnotes – Update 0.22 / Patch 0.22.1',
    url: 'https://steamcommunity.com/app/3026450/allnews/',
    note: 'Primärquelle für aktuelle Balance- und Patchänderungen. 0.22: Throwables profitieren nun von den meisten Waffen-Effekten; 0.22.1: Snake/Throwables-Fix.'
  },
  wikiItems: {
    title: 'Deadly Days: Roadtrip Community Wiki – Items',
    url: 'https://deadly-days-roadtrip.fandom.com/de/wiki/Items',
    note: 'Item-Kategorien, Formen, Beschreibungen. Das Wiki entsteht laut Startseite in Zusammenarbeit mit Pixelsplit, kann aber zeitlich hinter dem Spiel liegen.'
  },
  wikiChars: {
    title: 'Deadly Days: Roadtrip Community Wiki – Charaktere',
    url: 'https://deadly-days-roadtrip.fandom.com/de/wiki/Charaktere',
    note: 'Starter-Loadouts und Freischaltungen der älteren Charaktere.'
  },
  steamStore: {
    title: 'Steam Store – Deadly Days: Roadtrip',
    url: 'https://store.steampowered.com/app/3026450/Deadly_Days_Roadtrip/',
    note: 'Offizielle Beschreibung des Rucksack-/Adjazenz- und Crafting-Systems.'
  }
};

const rect = (w,h) => Array.from({length:h},(_,y)=>Array.from({length:w},(_,x)=>[x,y])).flat();
const SHAPES = {
  one: [[0,0]], twoH: rect(2,1), twoV: rect(1,2), square4: rect(2,2), rect6: rect(3,2), rect10: rect(5,2),
  T4: [[0,0],[1,0],[2,0],[1,1]], L4: [[0,0],[0,1],[0,2],[1,2]], J4: [[1,0],[1,1],[0,2],[1,2]],
  diag2: [[0,0],[1,1]], diag4: [[0,0],[1,1],[2,2],[3,3]], bagL3: [[0,0],[1,0],[1,1]]
};

const item = (id,name,shape,tags,opts={}) => ({
  id,name,shape,tags,rotatable: opts.rotatable ?? true, base: opts.base ?? 5,
  description: opts.description ?? '', support: opts.support ?? null, synergy: opts.synergy ?? 0,
  source: opts.source ?? 'wikiItems', confidence: opts.confidence ?? 'medium', hands: opts.hands ?? 0,
  recipe: opts.recipe ?? null, notes: opts.notes ?? '', exactMarker: opts.exactMarker ?? null
});

const ITEMS = {
  knabe_kola: item('knabe_kola','Knabe Kola',SHAPES.twoH,['weapon','throwable','triggerable','inflatable','explosive','aoe','signature'],{
    base:9,confidence:'high',description:'Alle 5 s und bei Auslösung: zieht nahe Gegner 3 s an und explodiert. 98 Grundschaden +2 pro Charakterlevel; 0,2 s Trigger-Cooldown.',
    notes:'Signaturitem von Mr. Präsi Sir. Seit 0.22 profitieren Throwables von den meisten Waffen-Effekten.',source:'wikiItems'
  }),
  pistol: item('pistol','Pistole',SHAPES.twoH,['weapon','ranged','singleTarget'],{base:4,confidence:'medium',hands:1,description:'Kompakte Einhand-Fernkampfwaffe und häufiger Starter.'}),
  grenade: item('grenade','Granate',SHAPES.one,['weapon','throwable','triggerable','inflatable','explosive','aoe'],{base:7,confidence:'medium',description:'Kompakte explosive Wurfwaffe; besonders wertvoll in Trigger-/Inflatable-Synergien.'}),
  firework: item('firework','Feuerwerk',SHAPES.one,['support','powerup','explosive'],{base:6,confidence:'high',support:['weapon'],synergy:7,description:'Markierte Waffen können bei Treffern explodieren und zusätzlichen Waffenschaden verursachen. Seit 0.22 können Throwables davon profitieren.'}),
  pump: item('pump','Fahrradpumpe',SHAPES.twoH,['support','powerup'],{base:7,confidence:'medium',support:['inflatable'],synergy:8,description:'Verstärkt markierte aufblasbare Items. Exakte Marker-Geometrie muss noch im Spiel vermessen werden.'}),
  running_shoes: item('running_shoes','Laufschuhe',SHAPES.square4,['support','footwear','movement'],{base:8,confidence:'high',support:['triggerable'],synergy:9,description:'Erhöht Bewegungsgeschwindigkeit. Nach 7 gelaufenen Metern werden markierte auslösbare Items ausgelöst.'}),
  rollerblades: item('rollerblades','Rollerblades',SHAPES.twoH,['support','footwear','movement'],{base:7,confidence:'high',support:['triggerable'],synergy:8,description:'Erhöht Bewegungsgeschwindigkeit; beim Sprint werden markierte auslösbare Items ausgelöst.'}),
  toolbelt: item('toolbelt','Werkzeuggürtel',SHAPES.twoH,['support','powerup','ability'],{base:7,confidence:'high',support:['triggerable'],synergy:8,description:'+1 Powerup-Slot (max. 4). Bei Fähigkeitsnutzung werden markierte auslösbare Items mehrfach über 2 s ausgelöst.'}),
  reserve_mag: item('reserve_mag','Reservemagazin',SHAPES.twoH,['support','powerup','firerate'],{base:5.5,confidence:'high',support:['ranged'],synergy:6,description:'Erhöht Angriffsgeschwindigkeit und Magazingröße markierter Fernkampfwaffen, verlängert aber die Nachladedauer.'}),
  boxing_glove: item('boxing_glove','Boxhandschuh',SHAPES.one,['support','powerup','melee'],{base:4,confidence:'high',support:['melee'],synergy:6,description:'Treffer markierter Nahkampfwaffen erhalten einen zusätzlichen Folgetreffer.'}),
  first_aid: item('first_aid','Erste-Hilfe-Kit',SHAPES.square4,['defense','healing'],{base:6,confidence:'high',description:'Heilt einen Teil des erlittenen Schadens verzögert zurück, sofern kein weiterer Schaden erlitten wird.'}),
  body_armor: item('body_armor','Körperpanzerung',SHAPES.square4,['defense','armor','survival'],{base:7,confidence:'high',description:'Fügt Panzerung hinzu. Jede Panzerung negiert einen Treffer, reduziert jedoch Bewegungsgeschwindigkeit; Regeneration zu Tagesbeginn.'}),
  heavy_boots: item('heavy_boots','Schwere Stiefel',SHAPES.square4,['defense','armor','footwear','triggerable'],{base:6.5,confidence:'high',description:'Panzerungs-Schuhwerk; auslösbar. Mehr Sicherheit, aber Bewegungskosten.'}),
  fire_shoes: item('fire_shoes','Feuerschuhe',SHAPES.twoH,['footwear','triggerable','inflatable','fire','aoe'],{base:6.5,confidence:'high',description:'Beim Sprint und bei Auslösung entsteht eine Feuerspur; kann Gegner entzünden.'}),
  blinking_shoes: item('blinking_shoes','Blinkende Schuhe',SHAPES.twoH,['footwear','triggerable','electric','aoe'],{base:6,confidence:'high',description:'Nach gelaufener Distanz und bei Auslösung wird Kettenblitzschaden verursacht.'}),
  socks: item('socks','Socken',SHAPES.one,['support','footwear'],{base:4.5,confidence:'high',support:['footwear'],synergy:5,description:'Erhöht Schaden und Bewegungsgeschwindigkeit von markiertem Schuhwerk; wirkt nicht auf andere Socken.'}),
  magnet: item('magnet','Magnet',SHAPES.one,['utility','inflatable','loot'],{base:4.5,confidence:'high',description:'Erhöht die Sammelreichweite um 150 %. Markierung mit einem weiteren Magneten hebt den Effekt auf.'}),
  wifi: item('wifi','WLAN-Repeater',SHAPES.twoH,['support','powerup','electric','complex'],{base:6,confidence:'high',support:['any'],synergy:4,description:'Überträgt auf sich angewendete Effekte auf markierte kompatible Items. Modellierung derzeit nur näherungsweise.'}),
  dumbbell: item('dumbbell','Hantel',SHAPES.diag2,['support','powerup','hands'],{base:5.5,confidence:'high',support:['weapon'],synergy:4.5,description:'Reduziert benötigte Hände markierter Waffen um 1, Minimum 1. Diagonale 2-Feld-Form.'}),
  robot_arm: item('robot_arm','Roboterarm',SHAPES.diag4,['powerup','hands','electric'],{base:6,confidence:'high',description:'Zählt als zusätzliche Hände beim Ausrüsten von Waffen. Diagonale 4-Feld-Form.'}),
  radar: item('radar','Radar',SHAPES.square4,['powerup','electric','loot','economy'],{base:4.5,confidence:'high',description:'Erhöht die Chance auf einen zweiten Airdrop pro markiertem elektrischen Item.'}),
  apple_turnover: item('apple_turnover','Apfeltasche',SHAPES.twoH,['powerup','inflatable','food','aoe','fire'],{base:5.5,confidence:'high',description:'Erzeugt eine Lavafläche in Größe der Sammelreichweite und entzündet Gegner.'}),
  void: item('void','Void',SHAPES.square4,['support','powerup','spaceScaling'],{base:7,confidence:'high',support:['weapon','triggerable'],synergy:6,description:'Erhöht Schaden markierter Waffen und auslösbarer Items abhängig von leeren Händen und leeren Rucksackslots.'}),
  dynamite: item('dynamite','Dynamit',SHAPES.twoH,['support','powerup','explosive'],{base:8,confidence:'high',support:['weapon'],synergy:8,description:'Stärkerer Explosions-Support für markierte Waffen.',recipe:['firework','grenade']}),
  glass_bottle: item('glass_bottle','Glasflasche',SHAPES.twoH,['weapon','throwable','triggerable','inflatable'],{base:5.5,confidence:'high',description:'Alle 2 s und bei Auslösung wird eine Flasche auf nahe Gegner geworfen; 0,2 s Trigger-Cooldown.'}),
  special_banana: item('special_banana','Spezialbanane',SHAPES.twoH,['weapon','throwable','triggerable','inflatable','explosive','aoe','signature'],{base:9,confidence:'high',description:'Alle 4 s und bei Auslösung: springt mehrfach und explodiert; Schaden skaliert pro Charakterlevel.'}),
  mechanical_egg: item('mechanical_egg','Mechanisches Ei',SHAPES.square4,['weapon','throwable','triggerable','inflatable','minion','signature'],{base:9,confidence:'high',description:'Wirft ein Ei, aus dem mechanische Diener schlüpfen. Diener profitieren von den meisten Waffen-Item-Effekten.'}),
  spacesuit: item('spacesuit','Raumanzug',SHAPES.rect6,['defense','armor','crit','signature'],{base:9,confidence:'high',description:'3 Panzerung und kritische Trefferchance pro Panzerung; Panzerung negiert Treffer, reduziert aber Bewegung.'}),
  revolver: item('revolver','Revolver',SHAPES.twoH,['weapon','ranged','singleTarget'],{base:6,confidence:'high',hands:1,description:'Einhand-Fernkampfwaffe mit hoher Schussgeschwindigkeit und langer Nachladedauer.'}),
  sawed_shotgun: item('sawed_shotgun','Abgesägte Schrotflinte',SHAPES.twoH,['weapon','ranged','aoe','closeRange'],{base:6,confidence:'high',hands:1,description:'Sehr stark auf kurze Distanz, eingeschränkt auf Reichweite.'}),
  baseball_bat: item('baseball_bat','Baseballschläger',rect(3,1),['weapon','melee'],{base:5,confidence:'high',hands:1,description:'Nahkampfwaffe; Nahkampfschadensreduktion pro Treffer.'}),
  uzi: item('uzi','UZI',SHAPES.T4,['weapon','ranged','firerate'],{base:7.5,confidence:'high',hands:1,description:'Schnelles Dauerfeuer mit geringerer Genauigkeit. T-Form.',recipe:['pistol','reserve_mag']}),
  minigun: item('minigun','Minigun',SHAPES.L4,['weapon','ranged','firerate'],{base:9,confidence:'high',hands:2,description:'Sehr hohe Schussgeschwindigkeit. L-Form.',recipe:['ak47','mp','reserve_mag']}),
  rocket_launcher: item('rocket_launcher','Raketenwerfer',SHAPES.rect10,['weapon','ranged','explosive','aoe'],{base:10,confidence:'high',hands:2,description:'Sehr große 5×2-Waffe; mächtige Raketen.',recipe:['grenade','pump','rifle']}),
  treasure: item('treasure','Schatzkiste',SHAPES.rect6,['powerup','economy','crit'],{base:5,confidence:'high',description:'Morgendliche Schrott-Zinsen und Crit-Multiplikator abhängig vom Schrottbestand.'}),
  pressure_cooker: item('pressure_cooker','Schnellkochtopf',SHAPES.rect10,['ability','aoe','explosive'],{base:8,confidence:'high',description:'Verleiht eine Fähigkeit für eine riesige Explosion; großer Platzbedarf.'}),
  funny_balloon: item('funny_balloon','Funny Balloon',SHAPES.twoH,['weapon','triggerable','explosive','aoe','signature'],{base:8.5,confidence:'medium',description:'Penny: setzt periodisch und bei Auslösung einen Ballon frei, der Gegner verfolgt und explodiert. Form noch nicht verifiziert.',source:'steam022'}),
  scythe: item('scythe','Sense',[[0,0],[1,0],[2,0],[0,1],[1,1],[0,2],[0,3],[0,4]],['weapon','melee','signature','revive'],{base:10,confidence:'medium',hands:2,description:'Signaturwaffe des Todes. Wiederbelebt nach dem Tod, wird dabei zerstört. Exakte Sensenform ist in der Wiki nur als „8 in Sensenform“ beschrieben; Geometrie hier vorläufig.'})
};

const EXPANSIONS = {
  bag2: {id:'bag2',name:'Rucksack +2 (2×1)',shape:SHAPES.twoH,type:'expansion',base:5.5},
  bag4: {id:'bag4',name:'Rucksack +4 (2×2)',shape:SHAPES.square4,type:'expansion',base:9},
  bagL3:{id:'bagL3',name:'Rucksack +3 (L)',shape:SHAPES.bagL3,type:'expansion',base:7}
};

const CHARACTERS = {
  survivor:{id:'survivor',name:'Überlebender',signature:null,start:['pistol'],focus:'Flexibler Allrounder',weights:{weapon:1,ranged:1,defense:.5},confidence:'high',source:'wikiChars',info:'Standardcharakter mit Pistole. Kein starkes Signatur-Synergiegerüst; dadurch offen für das stärkste Loot des Runs.'},
  president:{id:'president',name:'Mr. Präsi Sir',signature:'knabe_kola',start:['knabe_kola','pistol'],focus:'Kola-Trigger / AoE',weights:{throwable:2.2,triggerable:2.2,inflatable:1.9,explosive:1.7,aoe:1.4,support:1.1},confidence:'high',source:'wikiChars',info:'Knabe Kola ist auslösbar, aufblasbar und explosiv. Trigger- und Radius-Support sind daher überdurchschnittlich wertvoll.'},
  banana:{id:'banana',name:'Bananenperson',signature:'special_banana',start:['special_banana','pistol'],focus:'Triggerbare Explosiv-Banane',weights:{throwable:2,triggerable:2.1,inflatable:1.8,explosive:1.7,aoe:1.5},confidence:'high',source:'wikiChars',info:'Spezialbanane springt mehrfach und explodiert. Ähnliche Trigger-/Inflatable-Schiene wie Präsi, aber ohne Kola-Gruppierung.'},
  chicken:{id:'chicken',name:'Huhn',signature:'mechanical_egg',start:['mechanical_egg'],focus:'Diener / Trigger',weights:{minion:2.5,throwable:1.7,triggerable:1.8,inflatable:1.5,weapon:1.1},confidence:'high',source:'wikiItems',info:'Mechanisches Ei erzeugt Diener; die Diener profitieren laut Wiki von den meisten Waffen-Item-Effekten.'},
  astronaut:{id:'astronaut',name:'Astronaut',signature:'spacesuit',start:['spacesuit','pistol'],focus:'Panzerung → Krit',weights:{armor:2.3,crit:2,defense:1.7,movement:1.2,weapon:.8},confidence:'high',source:'wikiChars',info:'Raumanzug macht Panzerung zugleich defensiv und offensiv über Crit-Chance. Bewegung kann die Rüstungs-Verlangsamung ausgleichen.'},
  death:{id:'death',name:'Der Tod',signature:'scythe',start:['scythe'],focus:'Nahkampf / Wiederbelebung',weights:{melee:2.4,revive:2,defense:1.2,hands:.7},confidence:'medium',source:'wikiChars',info:'Sense ist große Nahkampf-Signaturwaffe und dient als einmalige Wiederbelebung. Exakte Form ist noch nicht vollständig vermessen.'},
  sheriff:{id:'sheriff',name:'Sheriff',signature:'revolver',start:['revolver'],focus:'Fernkampf / Reload',weights:{ranged:2.1,weapon:1.5,firerate:1.4,singleTarget:1.3,support:1},confidence:'medium',source:'wikiChars',info:'Startet mit Revolver und Sheriffstern. Der Helper gewichtet Fernkampf-Support hoch; Sheriffstern-Daten werden noch vervollständigt.'},
  penny:{id:'penny',name:'Penny the Clown',signature:'funny_balloon',start:['funny_balloon'],focus:'Ballon / Trigger / Explosion',weights:{triggerable:2.1,explosive:1.7,aoe:1.5,weapon:1.2},confidence:'medium',source:'steam022',info:'Offiziell seit 0.21: Funny Balloon wird periodisch und bei Auslösung freigesetzt, verfolgt Gegner und explodiert. Itemform noch offen.'}
};

const GOAL_WEIGHTS = {
  balanced:{weapon:.6,throwable:.5,defense:.5,support:.5,aoe:.5,movement:.35},
  horde:{aoe:2,explosive:1.5,throwable:1.1,triggerable:1.2,inflatable:.8,fire:.8,minion:1},
  boss:{singleTarget:2,ranged:1.3,firerate:1.5,weapon:.9,crit:1.3,support:.7},
  survival:{defense:2.3,healing:2.2,armor:2,movement:1.2,revive:2,utility:.5}
};
const STAGE_MOD = {early:{economy:1.1,loot:1,defense:.25},'early-mid':{economy:.7,loot:.7,weapon:.25},mid:{weapon:.4,support:.4,economy:.3},late:{weapon:.7,support:.7,defense:.5,economy:-.2}};

const state = {
  character:'president',stage:'early-mid',goal:'balanced',bag:new Set(),items:[],nextInstance:1
};

const key=(x,y)=>`${x},${y}`;
const parseKey=k=>k.split(',').map(Number);
const uniq=a=>[...new Set(a)];
const clamp=(v,a,b)=>Math.max(a,Math.min(b,v));
const tagLabel=t=>({weapon:'Waffe',ranged:'Fernkampf',melee:'Nahkampf',throwable:'Wurfwaffe',triggerable:'Auslösbar',inflatable:'Aufblasbar',explosive:'Explosion',aoe:'AoE',support:'Support',defense:'Defensiv',healing:'Heilung',armor:'Panzerung',footwear:'Schuhwerk',movement:'Bewegung',electric:'Elektrisch',powerup:'Powerup',signature:'Signatur',minion:'Diener',crit:'Krit',firerate:'Feuerrate',utility:'Utility',economy:'Ökonomie',loot:'Loot',ability:'Fähigkeit',fire:'Feuer',revive:'Revive',singleTarget:'Single Target',hands:'Hände',spaceScaling:'Leerraum-Skalierung'}[t]||t);

function normalizeShape(shape){
  const minX=Math.min(...shape.map(p=>p[0])), minY=Math.min(...shape.map(p=>p[1]));
  return shape.map(([x,y])=>[x-minX,y-minY]).sort((a,b)=>a[1]-b[1]||a[0]-b[0]);
}
function rotateShape(shape){return normalizeShape(shape.map(([x,y])=>[-y,x]));}
function shapeId(s){return normalizeShape(s).map(p=>p.join(':')).join('|')}
function rotations(item){
  let s=normalizeShape(item.shape), out=[];
  for(let i=0;i<(item.rotatable?4:1);i++){if(!out.some(o=>shapeId(o)===shapeId(s))) out.push(s);s=rotateShape(s)}
  return out;
}
function translate(shape,ox,oy){return shape.map(([x,y])=>[x+ox,y+oy])}
function orthNeighbors([x,y]){return [[x+1,y],[x-1,y],[x,y+1],[x,y-1]]}
function around8(cells){
  const own=new Set(cells.map(([x,y])=>key(x,y))), out=new Set();
  for(const [x,y] of cells) for(let dx=-1;dx<=1;dx++) for(let dy=-1;dy<=1;dy++) if(dx||dy){const k=key(x+dx,y+dy);if(!own.has(k))out.add(k)}
  return out;
}
function isTarget(support,target){
  const targets=support.support||[];
  return targets.includes('any') || targets.some(t=>target.tags.includes(t));
}
function instanceItem(inst){return ITEMS[inst.itemId]}
function area(item){return item.shape.length}
