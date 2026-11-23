// Verification scaffolding for the verify-northstar skill: the normal web config, served on 5274 and
// proxying /api to the verification API on 5206, so it never talks to a developer's API on 5106.
// No package imports here: they would resolve from this folder, which has no node_modules.
import config from "../../../web/vite.config";

export default {
  ...config,
  server: {
    port: 5274,
    strictPort: true,
    proxy: { "/api": "http://localhost:5206" },
  },
};
