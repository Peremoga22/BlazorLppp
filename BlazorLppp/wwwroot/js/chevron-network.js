// Canvas-based animated "digital network" overlay confined to the opaque
// silhouette of a shield/chevron PNG (A* "lightning path" style: glowing
// nodes, thin links, a handful of traveling pulses that light up the edge
// they cross and fade once they pass).
//
// One instance per <canvas>. create() does all loading/setup asynchronously
// and returns a handle synchronously; call handle.dispose() to stop the
// animation loop, disconnect observers and release canvas memory — this is
// what the Blazor component calls from DisposeAsync.

const YELLOW_TEST = (r, g, b) => r > 130 && g > 95 && b < 130 && (r - b) > 40 && (g - b) > 10;

function createSeededRandom(seed) {
    let s = seed % 2147483647;
    if (s <= 0) s += 2147483646;
    return function () {
        s = (s * 16807) % 2147483647;
        return (s - 1) / 2147483646;
    };
}

function loadImage(url) {
    return new Promise((resolve, reject) => {
        const img = new Image();
        img.onload = () => resolve(img);
        img.onerror = () => reject(new Error('failed to load image ' + url));
        img.src = url;
    });
}

// Decides, per pixel, whether it belongs to the shield. If the PNG carries
// real transparency we trust the alpha channel; otherwise (an opaque
// rectangular export) we key out the background by its corner color.
function buildMask(img, sampleW, sampleH) {
    const off = document.createElement('canvas');
    off.width = sampleW;
    off.height = sampleH;
    const octx = off.getContext('2d', { willReadFrequently: true });
    octx.drawImage(img, 0, 0, sampleW, sampleH);
    const data = octx.getImageData(0, 0, sampleW, sampleH).data;

    let hasTransparency = false;
    for (let i = 3; i < data.length; i += 4 * 37) {
        if (data[i] < 250) { hasTransparency = true; break; }
    }

    const mask = new Uint8Array(sampleW * sampleH);
    const idx = (x, y) => (y * sampleW + x) * 4;

    if (hasTransparency) {
        for (let y = 0; y < sampleH; y++) {
            for (let x = 0; x < sampleW; x++) {
                mask[y * sampleW + x] = data[idx(x, y) + 3] > 40 ? 1 : 0;
            }
        }
    } else {
        const corners = [idx(0, 0), idx(sampleW - 1, 0), idx(0, sampleH - 1), idx(sampleW - 1, sampleH - 1)];
        let br = 0, bg = 0, bb = 0;
        corners.forEach(c => { br += data[c]; bg += data[c + 1]; bb += data[c + 2]; });
        br /= 4; bg /= 4; bb /= 4;
        const tolerance = 26;
        for (let y = 0; y < sampleH; y++) {
            for (let x = 0; x < sampleW; x++) {
                const i = idx(x, y);
                const dr = data[i] - br, dg = data[i + 1] - bg, db = data[i + 2] - bb;
                const dist = Math.sqrt(dr * dr + dg * dg + db * db);
                mask[y * sampleW + x] = dist > tolerance ? 1 : 0;
            }
        }
    }

    return { mask, width: sampleW, height: sampleH };
}

// Pre-bakes a soft golden bloom that hugs only the warm/yellow pixels of the
// artwork (bow, arrow, flame, hand) so the glow never bleeds onto the red
// battlement or the dark olive field. Computed once, replayed every frame.
function buildGlowLayer(img, w, h) {
    const src = document.createElement('canvas');
    src.width = w;
    src.height = h;
    const sctx = src.getContext('2d', { willReadFrequently: true });
    sctx.drawImage(img, 0, 0, w, h);
    const imageData = sctx.getImageData(0, 0, w, h);
    const d = imageData.data;
    for (let i = 0; i < d.length; i += 4) {
        if (d[i + 3] < 40 || !YELLOW_TEST(d[i], d[i + 1], d[i + 2])) {
            d[i + 3] = 0;
        } else {
            d[i] = 255; d[i + 1] = 205; d[i + 2] = 90;
        }
    }
    sctx.putImageData(imageData, 0, 0);

    const glow = document.createElement('canvas');
    glow.width = w;
    glow.height = h;
    const gctx = glow.getContext('2d');
    gctx.filter = 'blur(' + Math.max(4, Math.round(w * 0.02)) + 'px)';
    gctx.drawImage(src, 0, 0);
    gctx.filter = 'none';
    return glow;
}

function isInsideMask(maskInfo, u, v) {
    const x = Math.min(maskInfo.width - 1, Math.max(0, Math.round(u * (maskInfo.width - 1))));
    const y = Math.min(maskInfo.height - 1, Math.max(0, Math.round(v * (maskInfo.height - 1))));
    return maskInfo.mask[y * maskInfo.width + x] === 1;
}

function generateNodes(maskInfo, count, rand) {
    const nodes = [];
    let attempts = 0;
    const maxAttempts = count * 80;
    while (nodes.length < count && attempts < maxAttempts) {
        attempts++;
        const u = rand();
        const v = rand();
        if (isInsideMask(maskInfo, u, v)) {
            nodes.push({ u, v, phase: rand() * Math.PI * 2, speed: 0.6 + rand() * 0.8 });
        }
    }
    return nodes;
}

function buildEdges(nodes) {
    const edgesSet = new Set();
    const edges = [];
    for (let i = 0; i < nodes.length; i++) {
        const distances = [];
        for (let j = 0; j < nodes.length; j++) {
            if (i === j) continue;
            const du = nodes[i].u - nodes[j].u;
            const dv = nodes[i].v - nodes[j].v;
            distances.push({ j, d: du * du + dv * dv });
        }
        distances.sort((a, b) => a.d - b.d);
        const take = 2 + (i % 2);
        for (let k = 0; k < Math.min(take, distances.length); k++) {
            const j = distances[k].j;
            const key = i < j ? i + '-' + j : j + '-' + i;
            if (!edgesSet.has(key)) {
                edgesSet.add(key);
                edges.push({ a: i, b: j, reveal: 0 });
            }
        }
    }
    return edges;
}

function buildAdjacency(nodeCount, edges) {
    const adjacency = Array.from({ length: nodeCount }, () => []);
    edges.forEach((e, idx) => {
        adjacency[e.a].push({ to: e.b, edgeIndex: idx });
        adjacency[e.b].push({ to: e.a, edgeIndex: idx });
    });
    return adjacency;
}

// Picks two well-separated nodes (max/min u+v, i.e. opposite corners of the
// point cloud) to play SOURCE and TARGET, then runs a real A* search over
// the node graph so the featured pulse follows an actual shortest path
// instead of wandering — this is the "lightning path" the whole effect is
// named after.
function pickEndpoints(nodes) {
    let sourceIdx = 0, targetIdx = 0, maxSum = -Infinity, minSum = Infinity;
    nodes.forEach((n, i) => {
        const sum = n.u + n.v;
        if (sum > maxSum) { maxSum = sum; sourceIdx = i; }
        if (sum < minSum) { minSum = sum; targetIdx = i; }
    });
    return { sourceIdx, targetIdx };
}

function runAStar(nodes, adjacency, source, target) {
    if (source === target) return [source];
    const dist = (a, b) => Math.hypot(nodes[a].u - nodes[b].u, nodes[a].v - nodes[b].v);
    const open = new Set([source]);
    const cameFrom = new Map();
    const gScore = new Map([[source, 0]]);
    const fScore = new Map([[source, dist(source, target)]]);

    while (open.size > 0) {
        let current = null, currentF = Infinity;
        for (const n of open) {
            const f = fScore.has(n) ? fScore.get(n) : Infinity;
            if (f < currentF) { currentF = f; current = n; }
        }
        if (current === target) {
            const path = [current];
            while (cameFrom.has(current)) { current = cameFrom.get(current); path.push(current); }
            path.reverse();
            return path;
        }
        open.delete(current);
        for (const { to } of adjacency[current]) {
            const tentativeG = (gScore.has(current) ? gScore.get(current) : Infinity) + dist(current, to);
            if (tentativeG < (gScore.has(to) ? gScore.get(to) : Infinity)) {
                cameFrom.set(to, current);
                gScore.set(to, tentativeG);
                fScore.set(to, tentativeG + dist(to, target));
                open.add(to);
            }
        }
    }
    return null;
}

function findPathEdgeIndices(path, edges) {
    const lookup = new Map();
    edges.forEach((e, idx) => {
        lookup.set(e.a + '-' + e.b, idx);
        lookup.set(e.b + '-' + e.a, idx);
    });
    const result = [];
    for (let i = 0; i < path.length - 1; i++) {
        result.push(lookup.get(path[i] + '-' + path[i + 1]));
    }
    return result;
}

class Pulse {
    constructor(adjacency, startNode, rand) {
        this.adjacency = adjacency;
        this.rand = rand;
        this.path = [startNode];
        this.segmentT = 0;
        this.speed = 0.9 + rand() * 0.6;
        this.dead = false;
        this._pickNext();
    }

    _pickNext() {
        const current = this.path[this.path.length - 1];
        const previous = this.path.length > 1 ? this.path[this.path.length - 2] : null;
        const options = this.adjacency[current].filter(o => o.to !== previous);
        const pool = options.length > 0 ? options : this.adjacency[current];
        if (pool.length === 0) {
            this.dead = true;
            this.nextNode = undefined;
            this.nextEdgeIndex = undefined;
            return;
        }
        const choice = pool[Math.floor(this.rand() * pool.length)];
        this.nextNode = choice.to;
        this.nextEdgeIndex = choice.edgeIndex;
    }

    update(dt, edges) {
        if (this.dead) return;
        this.segmentT += dt * this.speed;
        if (this.nextEdgeIndex !== undefined) {
            edges[this.nextEdgeIndex].reveal = Math.max(edges[this.nextEdgeIndex].reveal, Math.min(1, this.segmentT));
        }
        if (this.segmentT >= 1) {
            this.segmentT = 0;
            if (this.nextNode === undefined) { this.dead = true; return; }
            this.path.push(this.nextNode);
            if (this.path.length > 14) {
                this.dead = true;
                return;
            }
            this._pickNext();
        }
    }

    currentPosition(nodes) {
        const a = nodes[this.path[this.path.length - 1]];
        const b = nodes[this.nextNode];
        if (!b) return { u: a.u, v: a.v };
        return {
            u: a.u + (b.u - a.u) * this.segmentT,
            v: a.v + (b.v - a.v) * this.segmentT
        };
    }
}

export function create(canvas, imageUrl, options) {
    options = options || {};
    const nodeCountBase = options.nodeCount || 110;
    const maxPulses = options.maxPulses || 4;
    const darken = options.darken ?? 0.32;

    let disposed = false;
    let rafId = null;
    let resizeObserver = null;
    let mediaQuery = null;
    let mediaQueryHandler = null;
    let resizeTimer = null;

    let img = null;
    let maskInfo = null;
    let glowLayer = null;
    let nodes = [];
    let edges = [];
    let adjacency = [];
    let pulses = [];
    let lastTime = 0;
    let cssWidth = 0;
    let cssHeight = 0;
    let imgDrawRect = { x: 0, y: 0, w: 0, h: 0 };

    let sourceIdx = null;
    let targetIdx = null;
    let mainPath = null;
    let mainPathEdges = [];
    let mainPulse = { segment: 0, t: 0, pausedUntil: 0 };

    const ctx = canvas.getContext('2d');
    const rand = createSeededRandom(20260914);

    function computeDrawRect(cw, ch) {
        if (!img) return { x: 0, y: 0, w: cw, h: ch };
        const scale = Math.min(cw / img.width, ch / img.height);
        const w = img.width * scale;
        const h = img.height * scale;
        return { x: (cw - w) / 2, y: (ch - h) / 2, w, h };
    }

    function resize() {
        const rect = canvas.getBoundingClientRect();
        cssWidth = Math.max(1, Math.round(rect.width));
        cssHeight = Math.max(1, Math.round(rect.height));
        const dpr = Math.min(window.devicePixelRatio || 1, 2.5);
        canvas.width = Math.round(cssWidth * dpr);
        canvas.height = Math.round(cssHeight * dpr);
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        imgDrawRect = computeDrawRect(cssWidth, cssHeight);
    }

    function toCanvasXY(u, v) {
        return { x: imgDrawRect.x + u * imgDrawRect.w, y: imgDrawRect.y + v * imgDrawRect.h };
    }

    function drawBase() {
        ctx.clearRect(0, 0, cssWidth, cssHeight);
        if (!img) return;
        ctx.globalAlpha = 1;
        ctx.drawImage(img, imgDrawRect.x, imgDrawRect.y, imgDrawRect.w, imgDrawRect.h);
        ctx.globalCompositeOperation = 'source-atop';
        ctx.fillStyle = 'rgba(10, 12, 18,' + darken + ')';
        ctx.fillRect(imgDrawRect.x, imgDrawRect.y, imgDrawRect.w, imgDrawRect.h);

        // Faint tactical grid, clipped to the shield by 'source-atop' (it can
        // only paint over pixels the image just drew, so nothing leaks past
        // the silhouette onto the page background).
        const step = Math.max(18, Math.round(imgDrawRect.w / 14));
        ctx.strokeStyle = 'rgba(167, 160, 255, 0.07)';
        ctx.lineWidth = 1;
        ctx.beginPath();
        for (let gx = imgDrawRect.x; gx <= imgDrawRect.x + imgDrawRect.w; gx += step) {
            ctx.moveTo(gx, imgDrawRect.y);
            ctx.lineTo(gx, imgDrawRect.y + imgDrawRect.h);
        }
        for (let gy = imgDrawRect.y; gy <= imgDrawRect.y + imgDrawRect.h; gy += step) {
            ctx.moveTo(imgDrawRect.x, gy);
            ctx.lineTo(imgDrawRect.x + imgDrawRect.w, gy);
        }
        ctx.stroke();

        ctx.globalCompositeOperation = 'source-over';
        if (glowLayer) {
            ctx.globalAlpha = 0.55;
            ctx.drawImage(glowLayer, imgDrawRect.x, imgDrawRect.y, imgDrawRect.w, imgDrawRect.h);
            ctx.globalAlpha = 1;
        }
    }

    function drawEdgesAndNodes(time, animate) {
        edges.forEach(e => {
            if (e.reveal <= 0) return;
            const a = nodes[e.a], b = nodes[e.b];
            const pa = toCanvasXY(a.u, a.v);
            const pb = toCanvasXY(b.u, b.v);
            const t = Math.min(1, e.reveal);
            const bx = pa.x + (pb.x - pa.x) * t;
            const by = pa.y + (pb.y - pa.y) * t;
            ctx.strokeStyle = 'rgba(167, 160, 255, 0.22)';
            ctx.lineWidth = 1;
            ctx.beginPath();
            ctx.moveTo(pa.x, pa.y);
            ctx.lineTo(bx, by);
            ctx.stroke();
            if (animate) {
                e.reveal = Math.max(0, e.reveal - 0.0009);
            }
        });

        nodes.forEach(n => {
            const p = toCanvasXY(n.u, n.v);
            const flicker = animate ? 0.55 + 0.45 * Math.sin(time * 0.001 * n.speed + n.phase) : 0.6;
            ctx.beginPath();
            ctx.fillStyle = 'rgba(180, 174, 255,' + (0.25 + 0.35 * flicker) + ')';
            ctx.arc(p.x, p.y, 1.1 + flicker * 0.6, 0, Math.PI * 2);
            ctx.fill();
        });
    }

    function drawMainPath(time) {
        if (!mainPath || mainPath.length < 2) return;

        // The route itself: a faint constant thread the whole way, so the
        // eye can trace source → target even between pulse passes.
        ctx.strokeStyle = 'rgba(167, 160, 255, 0.28)';
        ctx.lineWidth = 1.5;
        ctx.beginPath();
        mainPath.forEach((idx, i) => {
            const p = toCanvasXY(nodes[idx].u, nodes[idx].v);
            if (i === 0) ctx.moveTo(p.x, p.y); else ctx.lineTo(p.x, p.y);
        });
        ctx.stroke();

        // The already-crossed portion glows brighter, like a completed circuit.
        if (mainPulse.segment > 0) {
            ctx.strokeStyle = 'rgba(196, 190, 255, 0.6)';
            ctx.lineWidth = 2;
            ctx.beginPath();
            for (let i = 0; i <= mainPulse.segment; i++) {
                const p = toCanvasXY(nodes[mainPath[i]].u, nodes[mainPath[i]].v);
                if (i === 0) ctx.moveTo(p.x, p.y); else ctx.lineTo(p.x, p.y);
            }
            ctx.stroke();
        }

        // The active segment, with the bright head pulse.
        const a = nodes[mainPath[mainPulse.segment]];
        const b = nodes[mainPath[Math.min(mainPulse.segment + 1, mainPath.length - 1)]];
        const pa = toCanvasXY(a.u, a.v);
        const pb = toCanvasXY(b.u, b.v);
        ctx.strokeStyle = 'rgba(214, 210, 255, 0.85)';
        ctx.lineWidth = 2.2;
        ctx.beginPath();
        ctx.moveTo(pa.x, pa.y);
        ctx.lineTo(pa.x + (pb.x - pa.x) * mainPulse.t, pa.y + (pb.y - pa.y) * mainPulse.t);
        ctx.stroke();

        const headX = pa.x + (pb.x - pa.x) * mainPulse.t;
        const headY = pa.y + (pb.y - pa.y) * mainPulse.t;
        ctx.save();
        ctx.shadowColor = 'rgba(214, 210, 255, 1)';
        ctx.shadowBlur = 16;
        ctx.fillStyle = 'rgba(240, 239, 255, 0.98)';
        ctx.beginPath();
        ctx.arc(headX, headY, 3, 0, Math.PI * 2);
        ctx.fill();
        ctx.restore();
    }

    function drawLabel(nodeIdx, text, accent) {
        if (nodeIdx === null) return;
        const n = nodes[nodeIdx];
        const p = toCanvasXY(n.u, n.v);

        ctx.save();
        ctx.font = '700 10px Arial, sans-serif';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        const paddingX = 7;
        const boxW = ctx.measureText(text).width + paddingX * 2;
        const boxH = 17;
        // Flip below the node whenever there isn't enough headroom above it,
        // so the label always stays inside the canvas instead of getting
        // clipped off the top edge (a real bug caught in testing).
        const spaceAbove = p.y - imgDrawRect.y;
        const dir = spaceAbove > boxH + 30 ? -1 : 1;
        const boxX = Math.min(Math.max(p.x - boxW / 2, 2), cssWidth - boxW - 2);
        const boxY = p.y + dir * 26 - boxH / 2;
        const boxCenterX = boxX + boxW / 2;

        ctx.beginPath();
        if (ctx.roundRect) { ctx.roundRect(boxX, boxY, boxW, boxH, 4); } else { ctx.rect(boxX, boxY, boxW, boxH); }
        ctx.fillStyle = 'rgba(10, 12, 18, 0.88)';
        ctx.strokeStyle = accent;
        ctx.lineWidth = 1.3;
        ctx.fill();
        ctx.stroke();
        ctx.fillStyle = accent;
        ctx.fillText(text, boxCenterX, boxY + boxH / 2 + 0.5);

        ctx.strokeStyle = accent;
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.moveTo(boxCenterX, dir === -1 ? boxY + boxH : boxY);
        ctx.lineTo(p.x, p.y - dir * 4);
        ctx.stroke();

        ctx.beginPath();
        ctx.fillStyle = accent;
        ctx.arc(p.x, p.y, 3, 0, Math.PI * 2);
        ctx.fill();
        ctx.restore();
    }

    function drawCaption() {
        if (!mainPath) return;
        const nodeNumber = mainPath[mainPulse.segment];
        ctx.save();
        ctx.font = '600 9px "Consolas", "Courier New", monospace';
        ctx.fillStyle = 'rgba(167, 160, 255, 0.4)';
        ctx.textAlign = 'left';
        ctx.textBaseline = 'alphabetic';
        ctx.fillText('МАРШРУТ · ВУЗОЛ #' + nodeNumber, imgDrawRect.x + 6, imgDrawRect.y + imgDrawRect.h - 7);
        ctx.restore();
    }

    function drawPulses() {
        pulses.forEach(p => {
            if (p.dead) return;
            if (p.nextEdgeIndex !== undefined) {
                const e = edges[p.nextEdgeIndex];
                const a = nodes[e.a], b = nodes[e.b];
                const pa = toCanvasXY(a.u, a.v);
                const pb = toCanvasXY(b.u, b.v);
                ctx.strokeStyle = 'rgba(196, 190, 255, 0.85)';
                ctx.lineWidth = 1.6;
                ctx.beginPath();
                ctx.moveTo(pa.x, pa.y);
                ctx.lineTo(pb.x, pb.y);
                ctx.stroke();
            }
            const pos = p.currentPosition(nodes);
            const cp = toCanvasXY(pos.u, pos.v);
            ctx.save();
            ctx.shadowColor = 'rgba(226, 224, 255, 0.95)';
            ctx.shadowBlur = 14;
            ctx.fillStyle = 'rgba(240, 239, 255, 0.95)';
            ctx.beginPath();
            ctx.arc(cp.x, cp.y, 2.4, 0, Math.PI * 2);
            ctx.fill();
            ctx.restore();
        });
    }

    function updateMainPulse(dt, now) {
        if (!mainPath || mainPath.length < 2) return;
        if (now < mainPulse.pausedUntil) return;

        mainPulse.t += dt * 0.7;
        const edgeIdx = mainPathEdges[mainPulse.segment];
        if (edgeIdx !== undefined) {
            edges[edgeIdx].reveal = Math.max(edges[edgeIdx].reveal, Math.min(1, mainPulse.t));
        }

        if (mainPulse.t >= 1) {
            mainPulse.t = 0;
            mainPulse.segment++;
            if (mainPulse.segment >= mainPath.length - 1) {
                // Reached the target — hold the glow briefly, then relaunch from source.
                mainPulse.segment = 0;
                mainPulse.pausedUntil = now + 1100;
            }
        }
    }

    function ensurePulses() {
        pulses = pulses.filter(p => !p.dead);
        while (pulses.length < maxPulses && nodes.length > 1) {
            const start = Math.floor(rand() * nodes.length);
            pulses.push(new Pulse(adjacency, start, rand));
        }
    }

    function frame(time) {
        if (disposed) return;
        const dt = lastTime ? Math.min(0.05, (time - lastTime) / 1000) : 0.016;
        lastTime = time;

        drawBase();
        pulses.forEach(p => p.update(dt, edges));
        ensurePulses();
        updateMainPulse(dt, time);
        drawEdgesAndNodes(time, true);
        drawMainPath(time);
        drawLabel(sourceIdx, 'ДЖЕРЕЛО', '#34d399');
        drawLabel(targetIdx, 'ЦІЛЬ', '#a7a0ff');
        drawCaption();
        drawPulses();

        rafId = window.requestAnimationFrame(frame);
    }

    function startLoop() {
        if (rafId !== null) return; // never run two loops at once
        lastTime = 0;
        rafId = window.requestAnimationFrame(frame);
    }

    function stopLoop() {
        if (rafId !== null) {
            window.cancelAnimationFrame(rafId);
            rafId = null;
        }
    }

    function renderStaticFrame() {
        stopLoop();
        drawBase();
        edges.forEach(e => { e.reveal = 1; });
        drawEdgesAndNodes(0, false);
        drawMainPath(0);
        drawLabel(sourceIdx, 'ДЖЕРЕЛО', '#34d399');
        drawLabel(targetIdx, 'ЦІЛЬ', '#a7a0ff');
        drawCaption();
    }

    function applyMotionPreference() {
        if (!maskInfo) return;
        if (mediaQuery && mediaQuery.matches) {
            renderStaticFrame();
        } else {
            startLoop();
        }
    }

    function rebuildGeometry() {
        if (!maskInfo) return;
        const area = cssWidth * cssHeight;
        const scaledCount = Math.round(Math.min(140, Math.max(80, nodeCountBase * Math.sqrt(area) / 700)));
        nodes = generateNodes(maskInfo, scaledCount, rand);
        edges = buildEdges(nodes);
        adjacency = buildAdjacency(nodes.length, edges);
        pulses = [];

        if (nodes.length > 1) {
            const endpoints = pickEndpoints(nodes);
            sourceIdx = endpoints.sourceIdx;
            targetIdx = endpoints.targetIdx;
            mainPath = runAStar(nodes, adjacency, sourceIdx, targetIdx);
            mainPathEdges = mainPath ? findPathEdgeIndices(mainPath, edges) : [];
        } else {
            sourceIdx = null;
            targetIdx = null;
            mainPath = null;
            mainPathEdges = [];
        }
        mainPulse = { segment: 0, t: 0, pausedUntil: 0 };
    }

    function onResize() {
        resize();
        clearTimeout(resizeTimer);
        resizeTimer = setTimeout(() => {
            if (disposed) return;
            rebuildGeometry();
            applyMotionPreference();
        }, 150);
    }

    (async () => {
        try {
            img = await loadImage(imageUrl);
        } catch (err) {
            console.warn('[chevron-network]', err.message);
            return;
        }
        if (disposed) return;

        const sampleW = 260;
        const sampleH = Math.max(1, Math.round(sampleW * (img.height / img.width)));
        maskInfo = buildMask(img, sampleW, sampleH);
        glowLayer = buildGlowLayer(img, Math.max(1, Math.round(img.width / 2)), Math.max(1, Math.round(img.height / 2)));

        resize();
        rebuildGeometry();

        mediaQuery = window.matchMedia('(prefers-reduced-motion: reduce)');
        mediaQueryHandler = () => applyMotionPreference();
        if (mediaQuery.addEventListener) {
            mediaQuery.addEventListener('change', mediaQueryHandler);
        } else if (mediaQuery.addListener) {
            mediaQuery.addListener(mediaQueryHandler);
        }

        resizeObserver = new ResizeObserver(onResize);
        resizeObserver.observe(canvas.parentElement || canvas);

        applyMotionPreference();
    })();

    return {
        dispose() {
            disposed = true;
            stopLoop();
            if (resizeObserver) {
                resizeObserver.disconnect();
                resizeObserver = null;
            }
            if (mediaQuery && mediaQueryHandler) {
                if (mediaQuery.removeEventListener) {
                    mediaQuery.removeEventListener('change', mediaQueryHandler);
                } else if (mediaQuery.removeListener) {
                    mediaQuery.removeListener(mediaQueryHandler);
                }
            }
            clearTimeout(resizeTimer);
            ctx.clearRect(0, 0, canvas.width, canvas.height);
        }
    };
}
