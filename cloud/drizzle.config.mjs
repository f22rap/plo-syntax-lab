import {defineConfig} from 'drizzle-kit';
import {fileURLToPath} from 'node:url';
export default defineConfig({dialect:'sqlite',schema:fileURLToPath(new URL('./db/schema.ts',import.meta.url)),out:fileURLToPath(new URL('./drizzle',import.meta.url))});
