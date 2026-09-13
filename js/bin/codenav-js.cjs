#!/usr/bin/env node
const fs = require('fs');
const path = require('path');
const parser = require('@babel/parser');
const traverse = require('@babel/traverse').default;

const command = process.argv[2];
const target = process.argv[3];
const extra = process.argv[4];

if (!command || command === '--help' || command === '-h') {
  console.log(`Usage: codenav-js <command> [arguments]
Commands:
  index [dir]                 List all JS/TS files with line counts and header descriptions
  skeleton <file>             Extract classes, methods, functions, and symbols with line ranges
  symbol <file> <name>        Extract specific function/class AST definition
`);
  process.exit(0);
}

function parseAst(filePath) {
  const code = fs.readFileSync(filePath, 'utf-8');
  try {
    return {
      code,
      ast: parser.parse(code, {
        sourceType: 'unambiguous',
        plugins: ['typescript', 'jsx', 'classProperties', 'decorators-legacy', 'exportDefaultFrom', 'dynamicImport']
      })
    };
  } catch (err) {
    console.error(`Error parsing AST for ${filePath}: ${err.message}`);
    return null;
  }
}

if (command === 'index') {
  const dir = target || '.';
  const results = [];
  function scan(current) {
    const entries = fs.readdirSync(current, { withFileTypes: true });
    for (const e of entries) {
      if (e.name === 'node_modules' || e.name === '.git' || e.name === 'bin' || e.name === 'obj') continue;
      const full = path.join(current, e.name);
      if (e.isDirectory()) scan(full);
      else if (/\.(js|mjs|cjs|ts|jsx|tsx)$/.test(e.name)) {
        const content = fs.readFileSync(full, 'utf-8');
        const lines = content.split('\n');
        let header = '(no description header)';
        for (let i = 0; i < Math.min(lines.length, 10); i++) {
          const l = lines[i].trim();
          if (l.startsWith('//') || l.startsWith('/*') || l.startsWith('*')) {
            const clean = l.replace(/^(\/\/|\/\*+|\*+|\*\/)/g, '').trim();
            if (clean.length > 5 && !clean.startsWith('@') && !clean.startsWith('eslint')) {
              header = clean;
              break;
            }
          }
        }
        results.push({ rel: path.relative(dir, full).replace(/\\/g, '/'), lines: lines.length, header });
      }
    }
  }
  scan(path.resolve(dir));
  console.log(`\n=== JS/TS FILE INDEX: (${results.length} files) [${path.resolve(dir)}] ===`);
  for (const r of results) {
    console.log(`${r.rel.padEnd(45)} (${String(r.lines).padStart(4)}L) | ${r.header}`);
  }
} else if (command === 'skeleton') {
  if (!target) { console.error('Missing target file'); process.exit(1); }
  const parsed = parseAst(path.resolve(target));
  if (!parsed) process.exit(1);
  const { code, ast } = parsed;
  const lines = code.split('\n');
  const symbols = [];

  traverse(ast, {
    ClassDeclaration(nodePath) {
      const name = nodePath.node.id ? nodePath.node.id.name : '(anonymous)';
      const start = nodePath.node.loc.start.line;
      const end = nodePath.node.loc.end.line;
      symbols.push({ type: 'CLASS', name, start, end });
    },
    ClassMethod(nodePath) {
      const name = nodePath.node.key.name || nodePath.node.key.value || '[computed]';
      const start = nodePath.node.loc.start.line;
      const end = nodePath.node.loc.end.line;
      const isStatic = nodePath.node.static ? 'static ' : '';
      const isAsync = nodePath.node.async ? 'async ' : '';
      const params = nodePath.node.params.map(p => p.name || p.type || 'param').join(', ');
      symbols.push({ type: 'METHOD', name: `${isStatic}${isAsync}${name}(${params})`, start, end });
    },
    FunctionDeclaration(nodePath) {
      const name = nodePath.node.id ? nodePath.node.id.name : '(anonymous)';
      const start = nodePath.node.loc.start.line;
      const end = nodePath.node.loc.end.line;
      const isAsync = nodePath.node.async ? 'async ' : '';
      const params = nodePath.node.params.map(p => p.name || p.type || 'param').join(', ');
      symbols.push({ type: 'FUNCTION', name: `${isAsync}${name}(${params})`, start, end });
    },
    VariableDeclarator(nodePath) {
      if (nodePath.node.init && (nodePath.node.init.type === 'ArrowFunctionExpression' || nodePath.node.init.type === 'FunctionExpression')) {
        const name = nodePath.node.id.name || '(anonymous)';
        const start = nodePath.node.loc.start.line;
        const end = nodePath.node.loc.end.line;
        const isAsync = nodePath.node.init.async ? 'async ' : '';
        const params = nodePath.node.init.params.map(p => p.name || p.type || 'param').join(', ');
        symbols.push({ type: 'ARROW_FN', name: `${isAsync}${name}(${params})`, start, end });
      }
    }
  });

  symbols.sort((a, b) => a.start - b.start);
  console.log(`\n=== JS SKELETON: ${path.basename(target)} (${symbols.length} symbols, ${lines.length} lines) [${target}] ===`);
  for (const s of symbols) {
    const range = `L${String(s.start).padStart(4)}-L${String(s.end).padStart(4)}`;
    console.log(`${range} | ${s.type.padEnd(9)} | ${s.name}`);
  }
} else if (command === 'symbol') {
  if (!target || !extra) { console.error('Usage: codenav-js symbol <file> <symbolName>'); process.exit(1); }
  const parsed = parseAst(path.resolve(target));
  if (!parsed) process.exit(1);
  const { code, ast } = parsed;
  const lines = code.split('\n');
  let matched = null;

  traverse(ast, {
    ClassDeclaration(nodePath) {
      if (nodePath.node.id?.name && nodePath.node.id.name.toLowerCase() === extra.toLowerCase()) {
        matched = { name: nodePath.node.id.name, type: 'CLASS', start: nodePath.node.loc.start.line, end: nodePath.node.loc.end.line };
      }
    },
    ClassMethod(nodePath) {
      const name = nodePath.node.key?.name || nodePath.node.key?.value;
      if (name && String(name).toLowerCase() === extra.toLowerCase()) {
        matched = { name: String(name), type: 'METHOD', start: nodePath.node.loc.start.line, end: nodePath.node.loc.end.line };
      }
    },
    FunctionDeclaration(nodePath) {
      if (nodePath.node.id?.name && nodePath.node.id.name.toLowerCase() === extra.toLowerCase()) {
        matched = { name: nodePath.node.id.name, type: 'FUNCTION', start: nodePath.node.loc.start.line, end: nodePath.node.loc.end.line };
      }
    },
    VariableDeclarator(nodePath) {
      if (nodePath.node.id?.name && nodePath.node.id.name.toLowerCase() === extra.toLowerCase()) {
        matched = { name: nodePath.node.id.name, type: 'VARIABLE/FN', start: nodePath.node.loc.start.line, end: nodePath.node.loc.end.line };
      }
    }
  });

  if (!matched) {
    console.error(`Symbol '${extra}' not found in ${target}`);
    process.exit(1);
  }
  console.log(`\n=== SYMBOL: ${matched.name} (${matched.type}, Lines ${matched.start}-${matched.end}) [${target}] ===`);
  for (let i = matched.start - 1; i < matched.end; i++) {
    console.log(`${String(i + 1).padStart(4)}: ${lines[i]}`);
  }
} else if (command === 'slice') {
  console.error("Error: 'slice' is disabled. Use 'skeleton <file>' to list AST symbols and 'symbol <file> <name>' to extract definitions.");
  process.exit(1);
} else {
  console.error(`Unknown command: '${command}'. Valid commands: index, skeleton, symbol`);
  process.exit(1);
}
