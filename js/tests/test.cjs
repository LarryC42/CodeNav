const assert = require('assert');
const path = require('path');
const { execSync } = require('child_process');

console.log('Running codenav-js AST unit tests...');
const script = path.join(__dirname, '../bin/codenav-js.cjs');

// Test 1: JS AST Skeleton extraction on router.js
const routerPath = 'c:/prj/eh/src/EventHorizon.Api/wwwroot/js/router.js';
const routerOut = execSync(`node "${script}" skeleton "${routerPath}"`, { encoding: 'utf-8' }).trim();
assert(routerOut.includes('CLASS     | Router'), 'Should extract Router class');
assert(routerOut.includes('METHOD    | async handleRoute'), 'Should extract handleRoute async method');

// Test 2: Symbol extraction
const symbolOut = execSync(`node "${script}" symbol "${routerPath}" constructor`, { encoding: 'utf-8' }).trim();
assert(symbolOut.includes('constructor(shell)'), 'Should extract constructor');

// Test 3: Slice command should be disabled
try {
  execSync(`node "${script}" slice "${routerPath}:11-15"`, { encoding: 'utf-8', stdio: 'pipe' });
  assert.fail('Slice should throw an error since it is disabled');
} catch (err) {
  assert(err.stderr.includes('slice\' is disabled') || err.message.includes('slice\' is disabled'), 'Slice should output disabled error');
}

console.log('All JS AST unit tests passed successfully!');
