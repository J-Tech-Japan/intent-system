#!/usr/bin/env node

'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const platformPackage = new Map([
  ['darwin:arm64', '@j-tech-japan/intent-cli-darwin-arm64'],
  ['linux:x64', '@j-tech-japan/intent-cli-linux-x64'],
  ['win32:x64', '@j-tech-japan/intent-cli-win32-x64'],
]);

function platformBinaryName() {
  return process.platform === 'win32' ? 'intent-cli.exe' : 'intent-cli';
}

function findPlatformBinary() {
  const key = `${process.platform}:${process.arch}`;
  const packageName = platformPackage.get(key);
  if (!packageName) {
    throw new Error(
      `intent-system does not ship a self-contained intent-cli binary for ${process.platform}/${process.arch}. ` +
      'Install JTechJapan.IntentSystem.Cli with dotnet or use a supported release platform.',
    );
  }

  const binaryName = platformBinaryName();
  let packageBinary;
  try {
    packageBinary = require.resolve(`${packageName}/bin/${binaryName}`);
  } catch {
    throw new Error(
      `The optional dependency ${packageName} is not installed for ${process.platform}/${process.arch}. ` +
      'Reinstall intent-system with optional dependencies enabled.',
    );
  }

  return packageBinary;
}

function pathHasIntentCli() {
  const pathValue = process.env.PATH || '';
  const names = process.platform === 'win32'
    ? ['intent-cli.exe', 'intent-cli.cmd', 'intent-cli']
    : ['intent-cli'];

  return pathValue.split(path.delimiter).filter(Boolean).some((directory) =>
    names.some((name) => {
      const candidate = path.join(directory, name);
      try {
        fs.accessSync(candidate, fs.constants.X_OK);
        return true;
      } catch {
        return false;
      }
    }),
  );
}

function wasInvokedByNpx() {
  const userAgent = process.env.npm_config_user_agent || process.env.NPM_CONFIG_USER_AGENT || '';
  return /(?:^|\s|\/)npx(?:\/|\s|$)/i.test(userAgent) || /^npx(?:\/|\s|$)/i.test(userAgent);
}

function emitNpxGuidanceOnce() {
  process.stderr.write(
    'For a persistent install, run: npm install -g intent-system; then use intent-cli.\n',
  );
}

function main() {
  const binary = findPlatformBinary();
  const result = spawnSync(binary, process.argv.slice(2), {
    env: process.env,
    stdio: 'inherit',
  });

  if (result.error) {
    throw result.error;
  }

  if (result.status === 0 && wasInvokedByNpx() && !pathHasIntentCli()) {
    emitNpxGuidanceOnce();
  }

  process.exitCode = result.status ?? 1;
}

try {
  main();
} catch (error) {
  process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
  process.exitCode = 1;
}
