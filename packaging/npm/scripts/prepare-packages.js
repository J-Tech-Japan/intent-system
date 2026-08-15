#!/usr/bin/env node

'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');

const packageRoot = path.resolve(__dirname, '..');
const platforms = [
  {
    rid: 'osx-arm64',
    directory: 'darwin-arm64',
    template: 'platforms/darwin-arm64/package.json',
    binary: 'intent-cli',
    npmPackage: '@j-tech-japan/intent-cli-darwin-arm64',
  },
  {
    rid: 'linux-x64',
    directory: 'linux-x64',
    template: 'platforms/linux-x64/package.json',
    binary: 'intent-cli',
    npmPackage: '@j-tech-japan/intent-cli-linux-x64',
  },
  {
    rid: 'win-x64',
    directory: 'win32-x64',
    template: 'platforms/win32-x64/package.json',
    binary: 'intent-cli.exe',
    npmPackage: '@j-tech-japan/intent-cli-win32-x64',
  },
];

function parseArguments(argv) {
  const options = { allowMissingBinaries: false };
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    if (argument === '--allow-missing-binaries') {
      options.allowMissingBinaries = true;
      continue;
    }
    if (argument === '--version' || argument === '--binary-root' || argument === '--output') {
      const value = argv[index + 1];
      if (!value || value.startsWith('--')) {
        throw new Error(`${argument} requires a value`);
      }
      options[argument.slice(2).replace('-', '')] = value;
      index += 1;
      continue;
    }
    throw new Error(`unknown argument: ${argument}`);
  }

  if (!options.version || !options.binaryroot || !options.output) {
    throw new Error('usage: prepare-packages.js --version VERSION --binary-root DIR --output DIR [--allow-missing-binaries]');
  }
  if (!/^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$/.test(options.version)) {
    throw new Error(`invalid npm version: ${options.version}`);
  }
  return options;
}

function readJson(filePath) {
  return JSON.parse(fs.readFileSync(filePath, 'utf8'));
}

function writeJson(filePath, value) {
  fs.writeFileSync(filePath, `${JSON.stringify(value, null, 2)}\n`);
}

function sha256(filePath) {
  return crypto.createHash('sha256').update(fs.readFileSync(filePath)).digest('hex');
}

function copyOrCreateBinary(source, destination, version, allowMissing, isWindows) {
  if (fs.existsSync(source)) {
    fs.copyFileSync(source, destination);
    if (!isWindows) {
      fs.chmodSync(destination, 0o755);
    }
    return 'release-asset';
  }
  if (!allowMissing) {
    throw new Error(`missing release binary for ${source}; use --allow-missing-binaries only for CI dry-run packaging`);
  }

  if (isWindows) {
    // A dry-run package is never published. Keep a deterministic placeholder
    // so npm pack and metadata/checksum checks exercise every platform shape.
    fs.writeFileSync(destination, `intent-cli ${version} (dry-run placeholder)\r\n`);
  } else {
    fs.writeFileSync(destination, `#!/bin/sh\nprintf 'intent-cli %s\\n' '${version}'\n`);
    fs.chmodSync(destination, 0o755);
  }
  return 'dry-run-placeholder';
}

function main() {
  const options = parseArguments(process.argv.slice(2));
  const binaryRoot = path.resolve(options.binaryroot);
  const outputRoot = path.resolve(options.output);
  fs.mkdirSync(outputRoot, { recursive: true });

  const mainPackage = readJson(path.join(packageRoot, 'package.json'));
  mainPackage.version = options.version;
  for (const platform of platforms) {
    mainPackage.optionalDependencies[platform.npmPackage] = options.version;
  }

  const mainOutput = path.join(outputRoot, 'intent-system');
  fs.mkdirSync(path.join(mainOutput, 'bin'), { recursive: true });
  fs.copyFileSync(path.join(packageRoot, 'bin', 'intent-cli.js'), path.join(mainOutput, 'bin', 'intent-cli.js'));
  fs.chmodSync(path.join(mainOutput, 'bin', 'intent-cli.js'), 0o755);
  fs.copyFileSync(path.join(packageRoot, 'README.md'), path.join(mainOutput, 'README.md'));
  writeJson(path.join(mainOutput, 'package.json'), mainPackage);

  const manifest = [];
  for (const platform of platforms) {
    const platformOutput = path.join(outputRoot, platform.directory);
    const binaryOutput = path.join(platformOutput, 'bin', platform.binary);
    fs.mkdirSync(path.dirname(binaryOutput), { recursive: true });
    const binarySource = path.join(binaryRoot, platform.rid, platform.binary);
    const sourceKind = copyOrCreateBinary(
      binarySource,
      binaryOutput,
      options.version,
      options.allowMissingBinaries,
      platform.binary.endsWith('.exe'),
    );
    const checksum = sha256(binaryOutput);
    fs.writeFileSync(`${binaryOutput}.sha256`, `${checksum}  ${platform.binary}\n`);

    const packageJson = readJson(path.join(packageRoot, platform.template));
    packageJson.version = options.version;
    packageJson.intentCli.version = options.version;
    packageJson.intentCli.binarySha256 = checksum;
    packageJson.intentCli.source = sourceKind === 'release-asset'
      ? 'self-contained GitHub Release asset'
      : 'CI-only dry-run placeholder; never publish';
    fs.copyFileSync(
      path.join(packageRoot, 'platforms', platform.directory, 'README.md'),
      path.join(platformOutput, 'README.md'),
    );
    writeJson(path.join(platformOutput, 'package.json'), packageJson);
    manifest.push({ package: platform.npmPackage, version: options.version, platform: platform.rid, source: sourceKind, sha256: checksum });
  }

  console.log(JSON.stringify({ main: 'intent-system', version: options.version, platforms: manifest }, null, 2));
}

try {
  main();
} catch (error) {
  console.error(error instanceof Error ? error.message : String(error));
  process.exitCode = 1;
}
