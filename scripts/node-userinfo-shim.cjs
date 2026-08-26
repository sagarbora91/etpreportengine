/*
 * Node 25 on this Windows image can make os.userInfo() fail with UV_ENOMEM even
 * when the build has memory available. Ionic's terminal helper calls it during
 * Capacitor startup. Preload this narrow fallback for Capacitor commands only.
 */
const os = require('node:os');
const originalUserInfo = os.userInfo;

try {
  originalUserInfo();
} catch (error) {
  if (!error || error.code !== 'ERR_SYSTEM_ERROR' || error.syscall !== 'uv_os_get_passwd') throw error;
  os.userInfo = function userInfoFallback() {
    return {
      uid: -1,
      gid: -1,
      username: process.env.USERNAME || 'windows-user',
      homedir: process.env.USERPROFILE || process.cwd(),
      shell: process.env.COMSPEC || 'cmd.exe'
    };
  };
}
