#!/usr/bin/env node

'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

function parseArguments(argv) {
  const options = {};
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    if (argument === '--main' || argument === '--platform' || argument === '--expected-version' || argument === '--work-dir') {
      const value = argv[index + 1];
      if (!value || value.startsWith('--')) throw new Error(`${argument} requires a value`);
      options[argument.slice(2).replaceAll('-', '')] = value;
      index += 1;
    } else {
      throw new Error(`unknown argument: ${argument}`);
    }
  }
  if (!options.main || !options.platform || !options.expectedversion || !options.workdir) {
    throw new Error('usage: smoke-packed.js --main MAIN_TGZ --platform PLATFORM_TGZ --expected-version VERSION --work-dir DIR');
  }
  return options;
}

function run(command, args, options = {}) {
  const result = spawnSync(command, args, { encoding: 'utf8', ...options });
  if (result.error) throw result.error;
  if (result.status !== 0) {
    throw new Error(`${command} ${args.join(' ')} failed (${result.status})\n${result.stdout}\n${result.stderr}`);
  }
  return result;
}

function main() {
  const options = parseArguments(process.argv.slice(2));
  const root = path.resolve(options.workdir);
  const emptyPath = path.join(root, 'empty-path');
  fs.mkdirSync(emptyPath, { recursive: true });
  fs.writeFileSync(
    path.join(root, 'package.json'),
    JSON.stringify({ name: 'g702-packed-smoke', version: '0.0.0', private: true }) + '\n',
  );
  run('npm', [
    'install', '--ignore-scripts', '--no-audit', '--no-fund', '--offline', '--omit=optional',
    path.resolve(options.main), path.resolve(options.platform),
  ], { cwd: root });
  const shim = path.join(root, 'node_modules', 'intent-system', 'bin', 'intent-cli.js');
  const result = run(process.execPath, [shim, '--version'], {
    cwd: root,
    env: {
      ...process.env,
      PATH: emptyPath,
      npm_config_user_agent: `npx/ci node/${process.version.slice(1)}`,
    },
  });
  const firstLine = result.stdout.trim().split(/\r?\n/, 1)[0];
  if (!new RegExp(`^intent-cli ${options.expectedversion}(?:$|[-+])`).test(firstLine)) {
    throw new Error(`packed shim returned '${firstLine}', expected ${options.expectedversion}`);
  }
  const guidance = result.stderr.trim().split(/\r?\n/).filter(Boolean);
  if (guidance.length !== 1 || !guidance[0].includes('npm install -g intent-system') || !guidance[0].includes('intent-cli')) {
    throw new Error(`packed npx invocation must emit exactly one guidance line; got ${JSON.stringify(guidance)}`);
  }
  console.log(`packed smoke passed: npx shim -> platform binary ${firstLine}; one guidance line`);
}

try {
  main();
} catch (error) {
  console.error(error instanceof Error ? error.message : String(error));
  process.exitCode = 1;
}
