import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';

import { parse } from '@babel/parser';

function getProperty(object, name) {
  assert.equal(object.type, 'ObjectExpression');
  const property = object.properties.find(
    (candidate) =>
      candidate.type === 'ObjectProperty' &&
      !candidate.computed &&
      ((candidate.key.type === 'Identifier' && candidate.key.name === name) ||
        (candidate.key.type === 'StringLiteral' && candidate.key.value === name)),
  );
  assert.ok(property, `Expected property '${name}'.`);
  return property.value;
}

function getString(node) {
  assert.equal(node.type, 'StringLiteral');
  return node.value;
}

function getStrings(node) {
  assert.equal(node.type, 'ArrayExpression');
  return node.elements.map((element) => {
    assert.ok(element);
    return getString(element);
  });
}

test('configures bounded production coverage output', async () => {
  const source = await readFile(new URL('../vite.config.ts', import.meta.url), 'utf8');
  const syntax = parse(source, {
    sourceType: 'module',
    plugins: ['typescript'],
  });
  const exported = syntax.program.body.find((node) => node.type === 'ExportDefaultDeclaration');
  assert.ok(exported);
  assert.equal(exported.declaration.type, 'CallExpression');
  assert.equal(exported.declaration.callee.type, 'Identifier');
  assert.equal(exported.declaration.callee.name, 'defineConfig');

  const config = exported.declaration.arguments[0];
  assert.ok(config && config.type !== 'SpreadElement' && config.type !== 'ArgumentPlaceholder');
  const testConfig = getProperty(config, 'test');
  const coverage = getProperty(testConfig, 'coverage');

  assert.equal(getString(getProperty(coverage, 'provider')), 'v8');
  assert.equal(getString(getProperty(coverage, 'reportsDirectory')), 'coverage');
  assert.deepEqual(getStrings(getProperty(coverage, 'reporter')), ['json', 'json-summary']);
  assert.deepEqual(getStrings(getProperty(coverage, 'include')), ['src/**/*.{ts,tsx}']);
  assert.equal(getProperty(getProperty(coverage, 'thresholds'), 'perFile').value, true);

  const coverageExclusions = getStrings(getProperty(coverage, 'exclude'));
  for (const pattern of [
    'src/**/*.test.{ts,tsx}',
    'src/**/*.spec.{ts,tsx}',
    'src/**/*.d.ts',
    'src/setupTests.ts',
    'src/**/generated/**',
    'src/**/*.generated.{ts,tsx}',
  ]) {
    assert.ok(coverageExclusions.includes(pattern));
  }

  assert.ok(getStrings(getProperty(testConfig, 'exclude')).includes('scripts/**'));
});
