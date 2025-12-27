import { fileURLToPath } from 'node:url';
import path from 'node:path';
import fs from 'node:fs/promises';
import { existsSync } from 'node:fs';

async function rmrf(target) {
  try {
    await fs.rm(target, { recursive: true, force: true });
  } catch {
    // ignore
  }
}

async function ensureDir(target) {
  await fs.mkdir(target, { recursive: true });
}

async function copyDir(src, dest) {
  await ensureDir(dest);
  const entries = await fs.readdir(src, { withFileTypes: true });
  await Promise.all(
    entries.map(async (entry) => {
      const srcPath = path.join(src, entry.name);
      const destPath = path.join(dest, entry.name);
      if (entry.isDirectory()) {
        await copyDir(srcPath, destPath);
      } else if (entry.isSymbolicLink()) {
        const real = await fs.readlink(srcPath);
        await fs.symlink(real, destPath);
      } else {
        await fs.copyFile(srcPath, destPath);
      }
    }),
  );
}

async function main() {
  const here = path.dirname(fileURLToPath(import.meta.url));
  const frontendRoot = path.resolve(here, '..');
  const src = path.resolve(frontendRoot, 'dist/spa');
  const dest = path.resolve(frontendRoot, '../YukariConnect/wwwroot');

  if (!existsSync(src)) {
    console.error(`[copy-wwwroot] 源目录不存在: ${src}`);
    process.exit(1);
  }

  console.log(`[copy-wwwroot] 清空目标目录: ${dest}`);
  await rmrf(dest);
  await ensureDir(dest);

  console.log(`[copy-wwwroot] 复制 ${src} -> ${dest}`);
  await copyDir(src, dest);

  console.log('[copy-wwwroot] 完成');
}

main().catch((err) => {
  console.error('[copy-wwwroot] 失败:', err?.message || err);
  process.exit(1);
});
