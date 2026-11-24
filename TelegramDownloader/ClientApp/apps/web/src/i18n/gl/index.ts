// Galician dictionary: every area file of this folder merged into one lazy chunk
const modules = import.meta.glob<{ default: Record<string, string> }>(['./*.ts', '!./index.ts'], { eager: true });

const gl: Record<string, string> = Object.assign({}, ...Object.values(modules).map((m) => m.default));

export default gl;
