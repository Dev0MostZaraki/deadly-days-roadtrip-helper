// Supplemental item data kept separate so live-recognition additions do not require rewriting the base dataset.
// Bohrer is verified from the current community item documentation: Powerup, electric, 1x1, marks ranged weapons for piercing.
ITEMS.drill = item('drill','Bohrer',SHAPES.one,['support','powerup','electric','ranged'],{
  base:6.2,
  confidence:'high',
  support:['ranged'],
  synergy:6.5,
  description:'Kugeln markierter Fernkampfwaffen durchschlagen zusätzliche Gegner. 1×1, elektrisch.',
  notes:'Im aktuellen Airdrop-Screenshot visuell bestätigt; Effekt/Form aus der Community-Dokumentation.'
});
