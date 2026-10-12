import assert from 'node:assert/strict';
import vm from 'node:vm';

// Execute the actual exported script; compare its DOM classes with the native canvas.
export function verifyReadonlyFocus({ html, cases }) {
  const scripts = [...html.matchAll(/<script([^>]*)>([\s\S]*?)<\/script>/g)];
  const payload = id => scripts.find(([_, attrs]) => attrs.includes(`id="${id}"`))[2];
  const graph = JSON.parse(payload('graphData'));
  function element(type, id) {
    const classes = new Set();
    return {
      dataset: { type, id }, value: '', style: {}, handlers: {},
      classList: {
        add: (...names) => names.forEach(name => classes.add(name)),
        remove: (...names) => names.forEach(name => classes.delete(name)),
        toggle: (name, on) => on ? classes.add(name) : classes.delete(name),
        contains: name => classes.has(name),
      },
      addEventListener(name, handler) { this.handlers[name] = handler; },
      closest() { return this; },
    };
  }
  const elements = ['node', 'group', 'edge'].flatMap(type => graph[type + 's'].map(item => element(type, item.id)));
  const controls = new Map();
  const get = id => {
    if (!controls.has(id)) controls.set(id, element('', id));
    return controls.get(id);
  };
  get('graphData').textContent = payload('graphData');
  get('focusData').textContent = payload('focusData');
  const svg = { viewBox: { baseVal: { x: 0, y: 0, width: 1000, height: 600 } },
    removeAttribute() {}, setAttribute(name, value) { this[name] = value; }, querySelectorAll: () => elements };
  get('stage').querySelector = () => svg;
  const documentHandlers = {};
  const document = { getElementById: get, body: element('', ''), documentElement: { style: {} }, addEventListener(name, handler) { (documentHandlers[name] ||= []).push(handler); } };
  vm.runInNewContext(scripts.find(([_, attrs]) => !attrs.trim())[2], {
    document, window: { addEventListener() {}, matchMedia: () => ({ matches: false, addEventListener() {} }) }, setTimeout,
  });
  for (const test of cases) {
    get('direction').value = test.direction;
    get('depth').value = String(test.depth);
    const selected = elements.find(el => `${el.dataset.type}:${el.dataset.id}` === test.selected);
    get('stage').handlers.click({ target: selected });
    for (const el of elements) {
      const { type, id } = el.dataset;
      const key = `${type}:${id}`;
      const primary = key === test.selected;
      const dim = type === 'edge' ? !test.edges.includes(id) : !test.entities.includes(key) || test.inactive.includes(id);
      const same = type === 'node' && !primary && test.same.includes(id);
      const related = type === 'edge' ? !primary && test.edges.includes(id) : !primary && !same && !dim && test.entities.includes(key);
      for (const [name, expected] of Object.entries({ 'is-selected': primary, 'is-dim': dim, 'is-same': same, 'is-related': related })) {
        assert.equal(el.classList.contains(name), expected, `${test.selected} / ${test.direction} / ${test.depth}: ${key} ${name}`);
      }
    }
  }
  get('clear').handlers.click();
  assert.ok(elements.every(el => ['is-selected', 'is-dim', 'is-same', 'is-related'].every(name => !el.classList.contains(name))));
  assert.match(html, /\[data-type='edge'\]\.is-dim\{display:none\}/);
  assert.match(html, /\.theme-dark \.sheet \[data-type='node'\]\.is-selected>:is\(rect,polygon,path\)/);
  const stage = get('stage');
  stage.getBoundingClientRect = () => ({ left: 0, top: 0, width: 1000, height: 600 });
  let captured = null;
  stage.setPointerCapture = id => captured = id;
  stage.hasPointerCapture = id => captured === id;
  stage.releasePointerCapture = () => captured = null;
  const event = (extra = {}) => ({ pointerId: 1, clientX: 100, clientY: 100, preventDefault() {}, ...extra });
  const keyboard = (name, target = { closest: () => null, matches: () => false }) =>
    documentHandlers[name].forEach(handler => handler({ code: 'Space', target, preventDefault() {} }));
  const pan = button => {
    const before = svg.viewBox;
    stage.handlers.pointerdown(event({ button }));
    assert.equal(captured, 1);
    stage.handlers.pointermove(event({ clientX: 150, clientY: 120 }));
    assert.notEqual(svg.viewBox, before);
    stage.handlers.pointerup(event({ type: 'pointerup' }));
    assert.equal(captured, null);
    assert.equal(stage.classList.contains('panning'), false);
  };
  pan(1);
  keyboard('keydown');
  assert.equal(stage.classList.contains('pan-ready'), true);
  pan(0);
  keyboard('keyup');
  assert.equal(stage.classList.contains('pan-ready'), false);
  stage.handlers.pointerdown(event({ button: 0 }));
  assert.equal(captured, null, 'Normal left click must not pan');
  keyboard('keydown', { closest: () => ({}), matches: () => true });
  assert.equal(stage.classList.contains('pan-ready'), false, 'Space on a control must retain its normal action');
  const width = () => Number(svg.viewBox.split(' ')[2]);
  const beforeZoom = width();
  stage.handlers.wheel(event({ deltaY: -100 }));
  assert.ok(width() < beforeZoom);
  stage.handlers.wheel(event({ deltaY: 100 }));
  assert.ok(width() > beforeZoom * .88);
  console.log('Readonly HTML space/left pan, middle pan and wheel zoom passed.');
  console.log(`Readonly HTML focus matches native canvas (${cases.length} scenarios).`);
}
