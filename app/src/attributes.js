// Player attributes as FM shows them, grouped like the player profile. `key` is
// the name the in-game plugin uses (FM's own property name without "Attribute").
// Everything is on FM's 1–20 scale.
const GROUPS = [
  {
    id: 'technical', name: 'Technical', outfield: true,
    keys: ['Corners', 'Crossing', 'Dribbling', 'Finishing', 'FirstTouch', 'FreeKicks', 'Heading', 'LongShots', 'LongThrows', 'Marking', 'Passing', 'Penalties', 'Tackling', 'Technique'],
  },
  {
    id: 'mental', name: 'Mental',
    keys: ['Aggression', 'Anticipation', 'Bravery', 'Composure', 'Concentration', 'Decisions', 'Determination', 'Flair', 'Leadership', 'OffTheBall', 'Positioning', 'Teamwork', 'Vision', 'WorkRate'],
  },
  {
    id: 'physical', name: 'Physical',
    keys: ['Acceleration', 'Agility', 'Balance', 'Jumping', 'NaturalFitness', 'Pace', 'Stamina', 'Strength'],
  },
  {
    id: 'goalkeeping', name: 'Goalkeeping', keeper: true,
    keys: ['Aerial', 'CommandOfArea', 'Communication', 'Eccentricity', 'Handling', 'Kicking', 'OneOnOnes', 'Punching', 'Reflexes', 'RushingOut', 'Throwing'],
  },
  {
    id: 'hidden', name: 'Hidden', note: 'Not shown in the game, but used by it.',
    keys: ['Consistency', 'Dirtiness', 'ImportantMatches', 'InjuryProneness', 'Versatility'],
  },
  {
    id: 'feet', name: 'Feet',
    keys: ['LeftFoot', 'RightFoot'],
  },
];

const LABELS = {
  FirstTouch: 'First Touch', FreeKicks: 'Free Kick Taking', LongShots: 'Long Shots', LongThrows: 'Long Throws',
  OffTheBall: 'Off The Ball', WorkRate: 'Work Rate', NaturalFitness: 'Natural Fitness', Aerial: 'Aerial Reach',
  CommandOfArea: 'Command Of Area', OneOnOnes: 'One On Ones', RushingOut: 'Rushing Out (Tendency)',
  Punching: 'Punching (Tendency)', ImportantMatches: 'Important Matches', InjuryProneness: 'Injury Proneness',
  LeftFoot: 'Left Foot', RightFoot: 'Right Foot', Corners: 'Corners', Penalties: 'Penalty Taking', Jumping: 'Jumping Reach',
};

const POSITIONS = ['GK', 'SW', 'DL', 'DC', 'DR', 'WBL', 'WBR', 'DM', 'ML', 'MC', 'MR', 'AML', 'AMC', 'AMR', 'ST'];

// Squad statuses the editor can set (FM's own codes). Other codes show as "Other".
const SQUAD_STATUSES = [
  [1, 'Star Player'], [2, 'Important Player'], [3, 'Regular Starter'], [4, 'Squad Player'], [7, 'Fringe Player'],
  [10, 'Breakthrough Prospect'], [11, 'Future Prospect'], [13, 'Youngster'], [15, 'First-Choice Goalkeeper'],
  [17, 'Domestic Cup Goalkeeper'], [20, 'Backup'], [21, 'Emergency Backup'],
];

const label = (k) => LABELS[k] || k.replace(/([a-z])([A-Z])/g, '$1 $2');

module.exports = { GROUPS, POSITIONS, SQUAD_STATUSES, label };
