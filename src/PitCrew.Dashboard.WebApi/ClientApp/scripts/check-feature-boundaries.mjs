import { parse } from '@babel/parser';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, extname, relative, resolve, sep } from 'node:path';
import { pathToFileURL } from 'node:url';

const sourceExtensions = new Set(['.ts', '.tsx']);

function sourceFiles(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = resolve(directory, entry.name);
    if (entry.isDirectory()) return sourceFiles(path);
    return sourceExtensions.has(extname(entry.name)) ? [path] : [];
  });
}

function importedFeature(root, file, specifier) {
  const featuresRoot = resolve(root, 'src/features');
  if (specifier.startsWith('@/features/')) return specifier.split('/')[2] ?? null;
  if (!specifier.startsWith('.')) return null;
  const target = relative(featuresRoot, resolve(dirname(file), specifier));
  if (target.startsWith('..') || target.startsWith(sep)) return null;
  return target.split(sep)[0] ?? null;
}

function importReferences(node, references = []) {
  if (!node || typeof node !== 'object') return references;
  if (
    (node.type === 'ImportDeclaration' ||
      node.type === 'ExportNamedDeclaration' ||
      node.type === 'ExportAllDeclaration') &&
    node.source?.type === 'StringLiteral'
  ) {
    references.push({ specifier: node.source.value, line: node.loc?.start.line ?? 1 });
  }
  if (
    node.type === 'CallExpression' &&
    node.callee?.type === 'Import' &&
    node.arguments[0]?.type === 'StringLiteral'
  ) {
    references.push({
      specifier: node.arguments[0].value,
      line: node.arguments[0].loc?.start.line ?? 1,
    });
  }
  if (node.type === 'ImportExpression' && node.source?.type === 'StringLiteral') {
    references.push({
      specifier: node.source.value,
      line: node.source.loc?.start.line ?? 1,
    });
  }
  for (const value of Object.values(node)) {
    if (Array.isArray(value)) {
      for (const child of value) importReferences(child, references);
    } else if (value && typeof value === 'object' && 'type' in value) {
      importReferences(value, references);
    }
  }
  return references;
}

/** Checks that a feature never imports from a sibling feature. */
export function checkFeatureBoundaries(root = process.cwd()) {
  const srcRoot = resolve(root, 'src');
  const featuresRoot = resolve(root, 'src/features');
  const registryFile = resolve(srcRoot, 'features.registry.ts');
  const files = statSync(featuresRoot).isDirectory() ? sourceFiles(featuresRoot) : [];
  const violations = [];

  for (const file of files) {
    const sourceFeature = relative(featuresRoot, file).split(sep)[0];
    const ast = parse(readFileSync(file, 'utf8'), {
      sourceType: 'module',
      plugins: ['typescript', 'jsx'],
    });
    for (const reference of importReferences(ast.program)) {
      const targetFeature = importedFeature(root, file, reference.specifier);
      if (targetFeature && targetFeature !== sourceFeature) {
        violations.push({
          file: relative(root, file).split(sep).join('/'),
          line: reference.line,
          sourceFeature,
          targetFeature,
          specifier: reference.specifier,
        });
      }
    }
  }

  const otherFiles = sourceFiles(srcRoot).filter((file) => {
    if (file.startsWith(featuresRoot + sep) || file === featuresRoot) return false;
    if (file === registryFile) return false;
    if (/\.(test|spec)\.[tj]sx?$/.test(file)) return false;
    if (file.includes(`${sep}__tests__${sep}`)) return false;
    return true;
  });

  for (const file of otherFiles) {
    const ast = parse(readFileSync(file, 'utf8'), {
      sourceType: 'module',
      plugins: ['typescript', 'jsx'],
    });
    for (const reference of importReferences(ast.program)) {
      const targetFeature = importedFeature(root, file, reference.specifier);
      if (targetFeature) {
        violations.push({
          file: relative(root, file).split(sep).join('/'),
          line: reference.line,
          sourceFeature: null,
          targetFeature,
          specifier: reference.specifier,
          registryBypass: true,
        });
      }
    }
  }

  return { scanned: files.length, violations };
}

function formatViolation(violation) {
  if (violation.registryBypass) {
    return [
      `  ${violation.file}:${violation.line}`,
      '    only src/features.registry.ts may import feature internals',
      `    import: ${violation.specifier}`,
    ].join('\n');
  }
  return [
    `  ${violation.file}:${violation.line}`,
    `    feature "${violation.sourceFeature}" imports from sibling feature "${violation.targetFeature}"`,
    `    import: ${violation.specifier}`,
  ].join('\n');
}

const isCli =
  process.argv[1] !== undefined && pathToFileURL(resolve(process.argv[1])).href === import.meta.url;

if (isCli) {
  const { violations, scanned } = checkFeatureBoundaries();
  if (violations.length > 0) {
    console.error(
      `check-feature-boundaries: ${violations.length} violation(s) in ${scanned} file(s):`,
    );
    for (const violation of violations) console.error(formatViolation(violation));
    process.exitCode = 1;
  } else {
    console.log(`check-feature-boundaries: scanned ${scanned} file(s); 0 violations.`);
  }
}
