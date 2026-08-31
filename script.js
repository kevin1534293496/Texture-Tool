const canvas = document.getElementById('mapCanvas');
const ctx = canvas.getContext('2d');

const sliders = {
  hill: document.getElementById('hillWeight'),
  river: document.getElementById('riverWeight'),
  plain: document.getElementById('plainWeight')
};

const labels = {
  hill: document.getElementById('hillValue'),
  river: document.getElementById('riverValue'),
  plain: document.getElementById('plainValue')
};

const seedDisplay = document.getElementById('seedDisplay');
const battleTypeDisplay = document.getElementById('battleType');
const generateBtn = document.getElementById('generateBtn');

const state = {
  hill: 34,
  river: 33,
  plain: 33,
  seed: 1,
  map: null
};

let regenerateTimer = null;
let resizeTimer = null;

const clamp = (value, min, max) => Math.max(min, Math.min(max, value));
const lerp = (a, b, t) => a + (b - a) * t;

function hashSeed(seed) {
  let h = 2166136261;
  const text = String(seed);
  for (let i = 0; i < text.length; i += 1) {
    h ^= text.charCodeAt(i);
    h = Math.imul(h, 16777619);
  }
  return h >>> 0;
}

function createRng(seed) {
  let t = hashSeed(seed) + 0x6d2b79f5;
  return () => {
    t += 0x6d2b79f5;
    let x = Math.imul(t ^ (t >>> 15), t | 1);
    x ^= x + Math.imul(x ^ (x >>> 7), x | 61);
    return ((x ^ (x >>> 14)) >>> 0) / 4294967296;
  };
}

function randomInt(rng, min, max) {
  return Math.floor(rng() * (max - min + 1)) + min;
}

function pick(rng, items) {
  return items[Math.floor(rng() * items.length)];
}

function randomSeed() {
  return Math.floor(Math.random() * 1_000_000_000);
}

function updateLabels() {
  labels.hill.textContent = String(state.hill);
  labels.river.textContent = String(state.river);
  labels.plain.textContent = String(state.plain);
}

function updateSliderValues() {
  sliders.hill.value = String(state.hill);
  sliders.river.value = String(state.river);
  sliders.plain.value = String(state.plain);
  updateLabels();
}

function rebalanceWeights(changedKey, rawValue) {
  const value = clamp(Math.round(rawValue), 0, 100);
  const otherKeys = Object.keys(state).filter((k) => ['hill', 'river', 'plain'].includes(k) && k !== changedKey);
  const oldOtherSum = state[otherKeys[0]] + state[otherKeys[1]];
  const remaining = 100 - value;

  state[changedKey] = value;

  if (oldOtherSum === 0) {
    state[otherKeys[0]] = Math.floor(remaining / 2);
    state[otherKeys[1]] = remaining - state[otherKeys[0]];
  } else {
    const first = Math.round((state[otherKeys[0]] / oldOtherSum) * remaining);
    state[otherKeys[0]] = first;
    state[otherKeys[1]] = remaining - first;
  }

  updateSliderValues();
}

function setupCanvas() {
  const ratio = window.devicePixelRatio || 1;
  const width = Math.max(640, canvas.clientWidth);
  const height = Math.max(420, canvas.clientHeight);
  canvas.width = Math.floor(width * ratio);
  canvas.height = Math.floor(height * ratio);
  ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
}

function createSmoothPath(controlPoints, samplesPerSegment = 24) {
  const pts = [];
  for (let i = 0; i < controlPoints.length - 1; i += 1) {
    const p0 = controlPoints[Math.max(0, i - 1)];
    const p1 = controlPoints[i];
    const p2 = controlPoints[i + 1];
    const p3 = controlPoints[Math.min(controlPoints.length - 1, i + 2)];

    for (let j = 0; j < samplesPerSegment; j += 1) {
      const t = j / samplesPerSegment;
      const t2 = t * t;
      const t3 = t2 * t;
      const x = 0.5 * ((2 * p1.x) + (-p0.x + p2.x) * t + (2 * p0.x - 5 * p1.x + 4 * p2.x - p3.x) * t2 + (-p0.x + 3 * p1.x - 3 * p2.x + p3.x) * t3);
      const y = 0.5 * ((2 * p1.y) + (-p0.y + p2.y) * t + (2 * p0.y - 5 * p1.y + 4 * p2.y - p3.y) * t2 + (-p0.y + 3 * p1.y - 3 * p2.y + p3.y) * t3);
      pts.push({ x, y });
    }
  }
  pts.push(controlPoints[controlPoints.length - 1]);
  return pts;
}

function edgePoint(width, height, side, t) {
  if (side === 'top') return { x: t * width, y: 0 };
  if (side === 'bottom') return { x: t * width, y: height };
  if (side === 'left') return { x: 0, y: t * height };
  return { x: width, y: t * height };
}

function riverDistance(point, path) {
  let best = Number.POSITIVE_INFINITY;
  for (let i = 0; i < path.length - 1; i += 1) {
    const a = path[i];
    const b = path[i + 1];
    const abx = b.x - a.x;
    const aby = b.y - a.y;
    const apx = point.x - a.x;
    const apy = point.y - a.y;
    const den = abx * abx + aby * aby || 1;
    const t = clamp((apx * abx + apy * aby) / den, 0, 1);
    const x = a.x + abx * t;
    const y = a.y + aby * t;
    const d = Math.hypot(point.x - x, point.y - y);
    if (d < best) best = d;
  }
  return best;
}

function samplePath(path, t) {
  const index = Math.floor(clamp(t, 0, 1) * (path.length - 1));
  return path[index];
}

function lineSegments(path) {
  const segments = [];
  for (let i = 0; i < path.length - 1; i += 1) {
    segments.push([path[i], path[i + 1]]);
  }
  return segments;
}

function ccw(a, b, c) {
  return (c.y - a.y) * (b.x - a.x) > (b.y - a.y) * (c.x - a.x);
}

function intersects(segA, segB) {
  const [a, b] = segA;
  const [c, d] = segB;
  return ccw(a, c, d) !== ccw(b, c, d) && ccw(a, b, c) !== ccw(a, b, d);
}

function isTributaryNatural(mainPath, tributary, others) {
  if (tributary.length < 4) return false;
  const end = tributary[tributary.length - 1];
  const prev = tributary[tributary.length - 2];

  let nearestIndex = 1;
  let nearestDist = Number.POSITIVE_INFINITY;
  for (let i = 1; i < mainPath.length - 1; i += 1) {
    const d = Math.hypot(mainPath[i].x - end.x, mainPath[i].y - end.y);
    if (d < nearestDist) {
      nearestDist = d;
      nearestIndex = i;
    }
  }

  const mainPrev = mainPath[Math.max(0, nearestIndex - 1)];
  const mainNext = mainPath[Math.min(mainPath.length - 1, nearestIndex + 1)];
  const mainVec = { x: mainNext.x - mainPrev.x, y: mainNext.y - mainPrev.y };
  const tribVec = { x: end.x - prev.x, y: end.y - prev.y };
  const dot = (mainVec.x * tribVec.x + mainVec.y * tribVec.y) / ((Math.hypot(mainVec.x, mainVec.y) * Math.hypot(tribVec.x, tribVec.y)) || 1);
  const angle = Math.acos(clamp(dot, -1, 1)) * (180 / Math.PI);

  if (angle > 78) return false;

  const tribSegments = lineSegments(tributary.slice(0, -1));
  const mainSegments = lineSegments(mainPath);
  for (const tSeg of tribSegments) {
    for (const mSeg of mainSegments) {
      if (intersects(tSeg, mSeg)) return false;
    }
  }

  for (const other of others) {
    const otherSegments = lineSegments(other.slice(0, -3));
    for (const tSeg of tribSegments) {
      for (const oSeg of otherSegments) {
        if (intersects(tSeg, oSeg)) return false;
      }
    }
  }

  return true;
}

function generateRiverSkeleton(rng, width, height, riverWeight) {
  const orientation = pick(rng, ['horizontal', 'diagonalA', 'diagonalB']);
  let start;
  let end;

  if (orientation === 'horizontal') {
    start = edgePoint(width, height, 'left', 0.22 + rng() * 0.56);
    end = edgePoint(width, height, 'right', 0.22 + rng() * 0.56);
  } else if (orientation === 'diagonalA') {
    start = edgePoint(width, height, 'top', 0.08 + rng() * 0.28);
    end = edgePoint(width, height, 'right', 0.62 + rng() * 0.3);
  } else {
    start = edgePoint(width, height, 'left', 0.6 + rng() * 0.3);
    end = edgePoint(width, height, 'bottom', 0.12 + rng() * 0.3);
  }

  const smoothness = lerp(0.22, 0.1, riverWeight / 100);
  const c1 = {
    x: lerp(start.x, end.x, 0.32) + (rng() - 0.5) * width * smoothness,
    y: lerp(start.y, end.y, 0.32) + (rng() - 0.5) * height * smoothness
  };
  const c2 = {
    x: lerp(start.x, end.x, 0.68) + (rng() - 0.5) * width * smoothness,
    y: lerp(start.y, end.y, 0.68) + (rng() - 0.5) * height * smoothness
  };

  const mainPath = createSmoothPath([start, c1, c2, end], 30);

  const targetTributaries = riverWeight < 20
    ? randomInt(rng, 0, 1)
    : riverWeight < 50
      ? randomInt(rng, 2, 4)
      : randomInt(rng, 4, 7);

  const tributaries = [];
  let attempts = 0;

  while (tributaries.length < targetTributaries && attempts < 80) {
    attempts += 1;

    const joinT = 0.14 + rng() * 0.72;
    const join = samplePath(mainPath, joinT);
    const sourceSide = pick(rng, ['top', 'bottom', 'left', 'right']);
    const source = edgePoint(width, height, sourceSide, 0.12 + rng() * 0.76);
    const bend1 = {
      x: lerp(source.x, join.x, 0.45) + (rng() - 0.5) * width * 0.08,
      y: lerp(source.y, join.y, 0.45) + (rng() - 0.5) * height * 0.08
    };
    const bend2 = {
      x: lerp(source.x, join.x, 0.74) + (rng() - 0.5) * width * 0.05,
      y: lerp(source.y, join.y, 0.74) + (rng() - 0.5) * height * 0.05
    };

    const path = createSmoothPath([source, bend1, bend2, join], 18);

    if (!isTributaryNatural(mainPath, path, tributaries)) continue;

    tributaries.push(path);

    const allowSecondOrder = riverWeight > 66 && rng() < 0.3;
    if (allowSecondOrder) {
      const split = samplePath(path, 0.58 + rng() * 0.2);
      const source2 = edgePoint(width, height, pick(rng, ['top', 'bottom', 'left', 'right']), 0.18 + rng() * 0.64);
      const branch = createSmoothPath([
        source2,
        { x: lerp(source2.x, split.x, 0.5) + (rng() - 0.5) * width * 0.06, y: lerp(source2.y, split.y, 0.5) + (rng() - 0.5) * height * 0.06 },
        split
      ], 14);
      if (isTributaryNatural(path, branch, tributaries)) {
        tributaries.push(branch);
      }
    }
  }

  const widthBase = lerp(5, 14, riverWeight / 100);

  return {
    orientation,
    mainPath,
    tributaries,
    widthBase,
    riverWeight
  };
}

function createBlob(rng, center, radius, stretch, clampRect) {
  const points = [];
  const count = randomInt(rng, 9, 14);

  for (let i = 0; i < count; i += 1) {
    const angle = (Math.PI * 2 * i) / count;
    const jitter = lerp(0.78, 1.18, rng());
    const rx = radius * jitter * stretch;
    const ry = radius * jitter / stretch;
    const x = center.x + Math.cos(angle) * rx;
    const y = center.y + Math.sin(angle) * ry;
    points.push({
      x: clamp(x, clampRect.pad, clampRect.width - clampRect.pad),
      y: clamp(y, clampRect.pad, clampRect.height - clampRect.pad)
    });
  }

  return {
    center,
    radius,
    points,
    tier: 'small'
  };
}

function createCoverageGrid(cols, rows) {
  return {
    cols,
    rows,
    values: Array(cols * rows).fill(0)
  };
}

function gridIndex(grid, x, y) {
  return y * grid.cols + x;
}

function markCoverage(grid, width, height, block, weight = 1) {
  const cellW = width / grid.cols;
  const cellH = height / grid.rows;
  for (let y = 0; y < grid.rows; y += 1) {
    for (let x = 0; x < grid.cols; x += 1) {
      const cx = (x + 0.5) * cellW;
      const cy = (y + 0.5) * cellH;
      const d = Math.hypot(cx - block.center.x, cy - block.center.y);
      if (d < block.radius * 1.08) {
        grid.values[gridIndex(grid, x, y)] += weight;
      }
    }
  }
}

function emptiestCells(grid) {
  const cells = [];
  for (let y = 0; y < grid.rows; y += 1) {
    for (let x = 0; x < grid.cols; x += 1) {
      cells.push({ x, y, value: grid.values[gridIndex(grid, x, y)] });
    }
  }
  cells.sort((a, b) => a.value - b.value);
  return cells;
}

function cellCenter(width, height, grid, x, y) {
  return {
    x: ((x + 0.5) / grid.cols) * width,
    y: ((y + 0.5) / grid.rows) * height
  };
}

function generateTerrainBlocks(rng, width, height, weights, river, coverageGrid) {
  const plainPalette = ['#8ea974', '#7f9f68', '#95b480'];
  const hillPalette = ['#9f8c64', '#8f7a56', '#a6946f'];
  const assistPalette = ['#b9bda3', '#b6b082', '#a8bb9f'];

  const blocks = {
    plain: [],
    hill: [],
    assist: []
  };

  const typeBias = {
    plain: 0.15 + 0.85 * (weights.plain / 100),
    hill: 0.15 + 0.85 * (weights.hill / 100)
  };

  const totalBias = typeBias.plain + typeBias.hill;
  const baseCount = 18 + Math.round((weights.plain + weights.hill) * 0.16);

  const plainCount = Math.max(2, Math.round((baseCount * typeBias.plain) / totalBias));
  const hillCount = Math.max(2, baseCount - plainCount);

  function placeBlock(type, tier) {
    const cells = emptiestCells(coverageGrid).slice(0, 14);
    const targetCell = pick(rng, cells);
    let center = cellCenter(width, height, coverageGrid, targetCell.x, targetCell.y);

    if (type === 'plain') {
      const anchorPath = rng() < 0.72 ? river.mainPath : pick(rng, river.tributaries.concat([river.mainPath]));
      const anchor = samplePath(anchorPath, rng());
      const angle = rng() * Math.PI * 2;
      const offset = lerp(18, 130, rng()) * lerp(0.65, 1.2, weights.plain / 100);
      center = {
        x: clamp(lerp(center.x, anchor.x + Math.cos(angle) * offset, 0.68), 18, width - 18),
        y: clamp(lerp(center.y, anchor.y + Math.sin(angle) * offset, 0.68), 18, height - 18)
      };
    } else if (type === 'hill') {
      const sidePush = rng() < 0.5 ? randomInt(rng, 0, 1) : randomInt(rng, 2, 3);
      const sideCenter = [
        { x: width * 0.16, y: height * (0.2 + rng() * 0.6) },
        { x: width * 0.84, y: height * (0.2 + rng() * 0.6) },
        { x: width * (0.2 + rng() * 0.6), y: height * 0.18 },
        { x: width * (0.2 + rng() * 0.6), y: height * 0.82 }
      ][sidePush];
      center = {
        x: clamp(lerp(center.x, sideCenter.x, 0.58), 18, width - 18),
        y: clamp(lerp(center.y, sideCenter.y, 0.58), 18, height - 18)
      };

      const d = riverDistance(center, river.mainPath);
      if (d < 42) {
        center = {
          x: clamp(center.x + (center.x > width / 2 ? 1 : -1) * 50, 18, width - 18),
          y: clamp(center.y + (center.y > height / 2 ? 1 : -1) * 34, 18, height - 18)
        };
      }
    }

    const radius = tier === 'large' ? lerp(44, 84, rng()) : tier === 'medium' ? lerp(26, 50, rng()) : lerp(14, 28, rng());
    const stretch = lerp(0.75, 1.35, rng());

    const blob = createBlob(rng, center, radius, stretch, { width, height, pad: 10 });
    blob.tier = tier;
    blob.type = type;
    blob.color = pick(rng, type === 'plain' ? plainPalette : hillPalette);

    if (type === 'hill' && riverDistance(center, river.mainPath) < blob.radius * 0.75) return null;

    if (type === 'plain') {
      markCoverage(coverageGrid, width, height, blob, 1.15);
    } else {
      markCoverage(coverageGrid, width, height, blob, 1.05);
    }

    return blob;
  }

  function tierCounts(count, weight) {
    const dominance = weight / 100;
    const large = Math.max(1, Math.round(1 + dominance * 3));
    const medium = Math.max(2, Math.round(count * lerp(0.28, 0.42, dominance)));
    const small = Math.max(1, count - large - medium);
    return { large, medium, small };
  }

  const plainTier = tierCounts(plainCount, weights.plain);
  const hillTier = tierCounts(hillCount, weights.hill);

  for (const [type, tierData] of [['plain', plainTier], ['hill', hillTier]]) {
    for (const tier of ['large', 'medium', 'small']) {
      for (let i = 0; i < tierData[tier]; i += 1) {
        let block = null;
        for (let tries = 0; tries < 8 && !block; tries += 1) {
          block = placeBlock(type, tier);
        }
        if (block) blocks[type].push(block);
      }
    }
  }

  const assistCount = 2 + Math.round(((weights.plain + weights.hill) / 200) * 5);
  for (let i = 0; i < assistCount; i += 1) {
    const emptyCell = pick(rng, emptiestCells(coverageGrid).slice(0, 10));
    const center = cellCenter(width, height, coverageGrid, emptyCell.x, emptyCell.y);
    const blob = createBlob(rng, center, lerp(12, 22, rng()), lerp(0.8, 1.2, rng()), { width, height, pad: 8 });
    blob.color = pick(rng, assistPalette);
    blob.type = 'assist';
    blob.tier = 'small';
    markCoverage(coverageGrid, width, height, blob, 0.7);
    blocks.assist.push(blob);
  }

  return blocks;
}

function qualityPass(rng, width, height, map) {
  const grid = map.coverageGrid;
  let cycles = 0;

  while (cycles < 14) {
    const cells = emptiestCells(grid);
    if (cells[0].value >= 0.8) break;

    const cell = pick(rng, cells.slice(0, 6));
    const center = cellCenter(width, height, grid, cell.x, cell.y);
    const type = riverDistance(center, map.river.mainPath) < 90 ? 'plain' : (rng() < 0.54 ? 'hill' : 'plain');
    const blob = createBlob(rng, center, lerp(16, 30, rng()), lerp(0.82, 1.22, rng()), { width, height, pad: 8 });
    blob.type = type;
    blob.tier = 'small';
    blob.color = type === 'plain' ? pick(rng, ['#8ea974', '#84a46e']) : pick(rng, ['#998760', '#8b7752']);
    map.blocks[type].push(blob);
    markCoverage(grid, width, height, blob, 0.9);
    cycles += 1;
  }

  map.evaluation = evaluateMap(width, height, map);
}

function evaluateMap(width, height, map) {
  const vals = map.coverageGrid.values;
  const occupied = vals.filter((v) => v > 0.6).length;
  const coverage = occupied / vals.length;

  const hasLarge = (arr) => arr.some((b) => b.tier === 'large');
  const hasMedium = (arr) => arr.some((b) => b.tier === 'medium');
  const hasSmall = (arr) => arr.some((b) => b.tier === 'small');

  return {
    coverage,
    tributaryCount: map.river.tributaries.length,
    hierarchyPass: hasLarge(map.blocks.plain) && hasMedium(map.blocks.plain) && hasSmall(map.blocks.plain) && hasLarge(map.blocks.hill) && hasMedium(map.blocks.hill) && hasSmall(map.blocks.hill),
    distributionBalance: Math.max(...vals) - Math.min(...vals) < 5.4,
    mainRiverStrength: map.river.widthBase
  };
}

function analyzeBattleType(weights, map) {
  const { hill, river, plain } = weights;
  const t = map.river.tributaries.length;
  const riverStrong = river > 62 || map.river.widthBase > 10;
  const plainStrong = plain > 55;
  const hillStrong = hill > 55;

  if (riverStrong && plainStrong) return t > 5 ? '河网阻击战' : '水岸攻防战';
  if (hillStrong && river > 35) return t > 3 ? '河谷争夺战' : '丘陵穿插战';
  if (hillStrong && plain < 22 && river < 24) return '山地遭遇战';
  if (plainStrong && river < 28) return '平原会战';
  if (Math.abs(hill - plain) < 16 && Math.abs(plain - river) < 16) return '混合地貌攻防战';
  return '复杂地形遭遇战';
}

function renderMap(width, height, map) {
  ctx.clearRect(0, 0, width, height);

  const gradient = ctx.createLinearGradient(0, 0, width, height);
  gradient.addColorStop(0, '#8b9297');
  gradient.addColorStop(1, '#777f84');
  ctx.fillStyle = gradient;
  ctx.fillRect(0, 0, width, height);

  function drawBlock(block, alpha, stroke = '#5d5d55') {
    ctx.beginPath();
    block.points.forEach((p, i) => {
      if (i === 0) ctx.moveTo(p.x, p.y);
      else ctx.lineTo(p.x, p.y);
    });
    ctx.closePath();
    ctx.fillStyle = block.color;
    ctx.globalAlpha = alpha;
    ctx.fill();
    ctx.globalAlpha = alpha * 0.45;
    ctx.strokeStyle = stroke;
    ctx.lineWidth = 1;
    ctx.stroke();
    ctx.globalAlpha = 1;
  }

  map.blocks.assist.forEach((b) => drawBlock(b, 0.55, '#868067'));
  map.blocks.plain.forEach((b) => drawBlock(b, 0.9, '#617259'));
  map.blocks.hill.forEach((b) => drawBlock(b, 0.94, '#66583f'));

  function drawRiver(path, widthBase, colorA, colorB) {
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';

    ctx.beginPath();
    path.forEach((p, i) => {
      if (i === 0) ctx.moveTo(p.x, p.y);
      else ctx.lineTo(p.x, p.y);
    });
    ctx.strokeStyle = colorA;
    ctx.lineWidth = widthBase;
    ctx.globalAlpha = 0.95;
    ctx.stroke();

    ctx.beginPath();
    path.forEach((p, i) => {
      if (i === 0) ctx.moveTo(p.x, p.y);
      else ctx.lineTo(p.x, p.y);
    });
    ctx.strokeStyle = colorB;
    ctx.lineWidth = Math.max(1.4, widthBase * 0.44);
    ctx.globalAlpha = 0.85;
    ctx.stroke();
    ctx.globalAlpha = 1;
  }

  drawRiver(map.river.mainPath, map.river.widthBase, '#5b88b0', '#87a9c6');
  map.river.tributaries.forEach((branch, index) => {
    const widthFactor = 0.35 + ((index % 5) * 0.05);
    drawRiver(branch, Math.max(1.8, map.river.widthBase * widthFactor), '#5b88b0', '#84a6c2');
  });
}

function generateMap(seed, weights, width, height) {
  const rng = createRng(seed);
  const river = generateRiverSkeleton(rng, width, height, weights.river);
  const coverageGrid = createCoverageGrid(8, 6);
  const blocks = generateTerrainBlocks(rng, width, height, weights, river, coverageGrid);

  const map = {
    seed,
    composition: {
      width,
      height,
      orientation: river.orientation
    },
    weights,
    river,
    blocks,
    coverageGrid
  };

  qualityPass(rng, width, height, map);
  return map;
}

function redrawWithSeed(seed, refreshType = true) {
  state.seed = seed;
  const width = canvas.clientWidth;
  const height = canvas.clientHeight;
  state.map = generateMap(seed, { hill: state.hill, river: state.river, plain: state.plain }, width, height);

  renderMap(width, height, state.map);
  seedDisplay.textContent = String(seed);
  if (refreshType) {
    battleTypeDisplay.textContent = analyzeBattleType({ hill: state.hill, river: state.river, plain: state.plain }, state.map);
  }
}

function triggerRegenerateWithNewSeed() {
  redrawWithSeed(randomSeed());
}

function debounceWeightGenerate() {
  clearTimeout(regenerateTimer);
  regenerateTimer = setTimeout(() => triggerRegenerateWithNewSeed(), 120);
}

Object.entries(sliders).forEach(([key, slider]) => {
  slider.addEventListener('input', () => {
    rebalanceWeights(key, Number(slider.value));
    debounceWeightGenerate();
  });
});

generateBtn.addEventListener('click', () => {
  triggerRegenerateWithNewSeed();
});

window.addEventListener('resize', () => {
  clearTimeout(resizeTimer);
  resizeTimer = setTimeout(() => {
    setupCanvas();
    redrawWithSeed(state.seed, false);
  }, 120);
});

setupCanvas();
updateSliderValues();
triggerRegenerateWithNewSeed();
