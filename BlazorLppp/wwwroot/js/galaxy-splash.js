import * as THREE from "https://unpkg.com/three@0.160.0/build/three.module.js";

const DEFAULTS = {
  count: 45000,
  size: 0.022,
  radius: 5.2,
  branches: 4,
  spin: 1.15,
  randomness: 0.32,
  randomnessPower: 2.8,
  insideColor: "#ffc857",
  outsideColor: "#3d6b8f",
};

let renderer;
let scene;
let camera;
let points;
let geometry;
let material;
let animationId;
let resizeHandler;

function disposeGalaxy() {
  if (points) {
    geometry?.dispose();
    material?.dispose();
    scene?.remove(points);
    points = null;
    geometry = null;
    material = null;
  }
}

function generateGalaxy(parameters) {
  disposeGalaxy();

  material = new THREE.PointsMaterial({
    size: parameters.size,
    sizeAttenuation: true,
    depthWrite: false,
    blending: THREE.AdditiveBlending,
    vertexColors: true,
  });

  geometry = new THREE.BufferGeometry();
  const positions = new Float32Array(parameters.count * 3);
  const colors = new Float32Array(parameters.count * 3);
  const colorInside = new THREE.Color(parameters.insideColor);
  const colorOutside = new THREE.Color(parameters.outsideColor);

  for (let i = 0; i < parameters.count; i++) {
    const i3 = i * 3;
    const radius = Math.random() * parameters.radius;
    const branchAngle = ((i % parameters.branches) / parameters.branches) * Math.PI * 2;
    const spinAngle = radius * parameters.spin;

    const randomX =
      Math.pow(Math.random(), parameters.randomnessPower) *
      (Math.random() < 0.5 ? 1 : -1) *
      parameters.randomness *
      radius;
    const randomY =
      Math.pow(Math.random(), parameters.randomnessPower) *
      (Math.random() < 0.5 ? 1 : -1) *
      parameters.randomness *
      radius *
      0.35;
    const randomZ =
      Math.pow(Math.random(), parameters.randomnessPower) *
      (Math.random() < 0.5 ? 1 : -1) *
      parameters.randomness *
      radius;

    positions[i3] = Math.cos(branchAngle + spinAngle) * radius + randomX;
    positions[i3 + 1] = randomY;
    positions[i3 + 2] = Math.sin(branchAngle + spinAngle) * radius + randomZ;

    const mixedColor = colorInside.clone();
    mixedColor.lerp(colorOutside, radius / parameters.radius);
    colors[i3] = mixedColor.r;
    colors[i3 + 1] = mixedColor.g;
    colors[i3 + 2] = mixedColor.b;
  }

  geometry.setAttribute("position", new THREE.BufferAttribute(positions, 3));
  geometry.setAttribute("color", new THREE.BufferAttribute(colors, 3));

  points = new THREE.Points(geometry, material);
  scene.add(points);
}

function tick() {
  if (!points || !renderer || !scene || !camera) {
    return;
  }

  points.rotation.y += 0.0018;
  points.rotation.x = Math.sin(performance.now() * 0.00015) * 0.08;
  renderer.render(scene, camera);
  animationId = requestAnimationFrame(tick);
}

export function startGalaxy(canvasOrId) {
  stopGalaxy();

  const canvas = typeof canvasOrId === "string"
    ? document.getElementById(canvasOrId)
    : canvasOrId;

  if (!canvas) {
    return;
  }

  const width = canvas.clientWidth || window.innerWidth;
  const height = canvas.clientHeight || window.innerHeight;

  scene = new THREE.Scene();
  camera = new THREE.PerspectiveCamera(65, width / height, 0.1, 100);
  camera.position.set(0, 3.2, 5.6);
  camera.lookAt(0, 0, 0);

  renderer = new THREE.WebGLRenderer({
    canvas,
    antialias: true,
    alpha: false,
    powerPreference: "high-performance",
  });
  renderer.setSize(width, height, false);
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
  renderer.setClearColor("#000000");

  generateGalaxy(DEFAULTS);

  resizeHandler = () => {
    if (!renderer || !camera || !canvas) {
      return;
    }

    const nextWidth = canvas.clientWidth || window.innerWidth;
    const nextHeight = canvas.clientHeight || window.innerHeight;
    camera.aspect = nextWidth / nextHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(nextWidth, nextHeight, false);
  };

  window.addEventListener("resize", resizeHandler);
  tick();
}

export function stopGalaxy() {
  if (animationId) {
    cancelAnimationFrame(animationId);
    animationId = null;
  }

  if (resizeHandler) {
    window.removeEventListener("resize", resizeHandler);
    resizeHandler = null;
  }

  disposeGalaxy();

  if (renderer) {
    renderer.dispose();
    renderer = null;
  }

  scene = null;
  camera = null;
}

window.galaxySplash = {
  start: startGalaxy,
  stop: stopGalaxy,
};
