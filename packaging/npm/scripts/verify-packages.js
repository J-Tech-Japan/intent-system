#!/usr/bin/env node

'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');

const provenanceRepositoryUrl = 'https://github.com/J-Tech-Japan/intent-system';
const platforms = [
  { directory: 'darwin-arm64', package: '@j-tech-japan/intent-cli-darwin-arm64', rid: 'osx-arm64', binary: 'intent-cli' },
  { directory: 'linux-x64', package: '@j-tech-japan/intent-cli-linux-x64', rid: 'linux-x64', binary: 'intent-cli' },
  { directory: 'win32-x64', package: '@j-tech-japan/intent-cli-win32-x64', rid: 'win-x64', binary: 'intent-cli.exe' },
];

function parseArguments(argv) {
  const options = { requireRealBinaries: false };
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    if (argument === '--require-real-binaries') {
      options.requireRealBinaries = true;
      continue;
    }
    if (argument === '--packages-dir' || argument === '--expected-version' || argument === '--version-output') {
      const value = argv[index + 1];
      if (!value || value.startsWith('--')) {
        throw new Error(`${argument} requires a value`);
      }
      options[argument.slice(2).replaceAll('-', '')] = value;
      index += 1;
      continue;
    }
    throw new Error(`unknown argument: ${argument}`);
  }
  if (!options.packagesdir || !options.expectedversion) {
    throw new Error('usage: verify-packages.js --packages-dir DIR --expected-version VERSION [--version-output FILE] [--require-real-binaries]');
  }
  return options;
}

function readJson(filePath) {
  return JSON.parse(fs.readFileSync(filePath, 'utf8'));
}

function digest(filePath) {
  return crypto.createHash('sha256').update(fs.readFileSync(filePath)).digest('hex');
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

function main() {
  const options = parseArguments(process.argv.slice(2));
  const root = path.resolve(options.packagesdir);
  const mainPackagePath = path.join(root, 'intent-system', 'package.json');
  const mainPackage = readJson(mainPackagePath);
  assert(mainPackage.name === 'intent-system', 'main package name drifted');
  assert(mainPackage.version === options.expectedversion, `main package version ${mainPackage.version} != ${options.expectedversion}`);
  assert(mainPackage.bin?.['intent-cli'] === 'bin/intent-cli.js', 'main package must expose the intent-cli shim');
  assert(mainPackage.repository?.url === 'git+https://github.com/J-Tech-Japan/intent-system.git', 'main package repository.url drifted');
  assert(!mainPackage.scripts?.postinstall, 'main package must not have a postinstall hook');

  for (const platform of platforms) {
    const directory = path.join(root, platform.directory);
    const packageJson = readJson(path.join(directory, 'package.json'));
    assert(packageJson.name === platform.package, `${platform.rid} package name drifted`);
    assert(packageJson.version === options.expectedversion, `${platform.rid} package version drifted`);
    assert(packageJson.repository?.url === provenanceRepositoryUrl, `${platform.rid} package repository.url must match npm provenance source`);
    assert(packageJson.intentCli?.version === options.expectedversion, `${platform.rid} recorded binary version drifted`);
    assert(packageJson.intentCli?.platform === platform.rid, `${platform.rid} platform metadata drifted`);
    assert(!packageJson.scripts?.postinstall, `${platform.rid} package must not have a postinstall hook`);
    assert(mainPackage.optionalDependencies?.[platform.package] === options.expectedversion, `${platform.rid} optional dependency version drifted`);

    const binary = path.join(directory, 'bin', platform.binary);
    const sidecar = `${binary}.sha256`;
    assert(fs.existsSync(binary), `${platform.rid} binary is missing`);
    assert(fs.existsSync(sidecar), `${platform.rid} checksum sidecar is missing`);
    const actual = digest(binary);
    const declared = packageJson.intentCli.binarySha256;
    assert(declared === actual, `${platform.rid} declared checksum does not match binary`);
    assert(fs.readFileSync(sidecar, 'utf8').startsWith(`${actual}  ${platform.binary}`), `${platform.rid} sidecar checksum does not match binary`);
    if (options.requireRealBinaries) {
      assert(packageJson.intentCli.source === 'self-contained GitHub Release asset', `${platform.rid} is not a release binary`);
    }
  }

  if (options.versionoutput) {
    const firstLine = fs.readFileSync(options.versionoutput, 'utf8').split(/\r?\n/, 1)[0];
    assert(new RegExp(`^intent-cli ${options.expectedversion}(?:$|[-+])`).test(firstLine), `binary version output '${firstLine}' does not match ${options.expectedversion}`);
  }

  console.log(`verified intent-system ${options.expectedversion}: main shim, ${platforms.length} platform packages, checksums, and version identity`);
}

try {
  main();
} catch (error) {
  console.error(error instanceof Error ? error.message : String(error));
  process.exitCode = 1;
}
