<template>
  <q-page class="q-pa-md">
    <div class="q-mb-md">
      <div class="text-h5 text-primary text-center">🎮 YukariConnect 控制台</div>
    </div>

    <!-- 状态 -->
    <q-card flat bordered class="q-mb-md">
      <q-card-section>
        <div class="text-subtitle1 q-mb-sm">📊 当前状态</div>
        <div class="row q-col-gutter-md">
          <div class="col-12 col-sm-3">
            <q-item class="bg-indigo-10 text-white rounded-borders">
              <q-item-section>
                <div class="text-caption">状态</div>
                <div
                  class="text-weight-bold"
                  :class="{
                    'text-negative': room.state === 'Error',
                    'text-positive': room.state !== 'Idle' && room.state !== 'Error'
                  }"
                >
                  {{ room.state || 'Idle' }}
                </div>
              </q-item-section>
            </q-item>
          </div>
          <div class="col-12 col-sm-3">
            <q-item class="bg-indigo-10 text-white rounded-borders">
              <q-item-section>
                <div class="text-caption">角色</div>
                <div class="text-weight-bold">{{ room.role || '-' }}</div>
              </q-item-section>
            </q-item>
          </div>
          <div class="col-12 col-sm-3">
            <q-item class="bg-indigo-10 text-white rounded-borders">
              <q-item-section>
                <div class="text-caption">MC 端口</div>
                <div class="text-weight-bold">{{ room.minecraftPort ?? '-' }}</div>
              </q-item-section>
            </q-item>
          </div>
          <div class="col-12 col-sm-3">
            <q-item class="bg-indigo-10 text-white rounded-borders">
              <q-item-section>
                <div class="text-caption">玩家数量</div>
                <div class="text-weight-bold">{{ room.players.length }}</div>
              </q-item-section>
            </q-item>
          </div>
        </div>
      </q-card-section>
    </q-card>

    <!-- 房间码（仅主机） -->
    <q-card
      flat
      bordered
      class="q-mb-md"
      v-show="room.roomCode && room.role === 'HostCenter'"
    >
      <q-card-section class="text-center">
        <div class="text-caption text-grey-6">房间码 (分享给好友)</div>
        <div class="room-code q-mt-sm" @click="copyRoomCode">
          {{ room.roomCode }}
        </div>
        <q-btn
          color="secondary"
          class="q-mt-sm"
          :label="copied ? '已复制!' : '复制房间码'"
          @click="copyRoomCode"
        />
      </q-card-section>
    </q-card>

    <!-- 主机控制 -->
    <q-card flat bordered class="q-mb-md">
      <q-card-section>
        <div class="text-subtitle1 q-mb-sm">🏠 创建房间 (Host)</div>
        <div class="row q-col-gutter-md">
          <div class="col-12 col-sm-6">
            <q-input
              v-model="hostPlayerName"
              label="玩家名称"
              dense
              outlined
              :disable="isRunning"
            />
          </div>
        </div>
        <div class="row q-col-gutter-sm q-mt-sm">
          <div class="col-auto">
            <q-btn
              color="primary"
              label="创建房间"
              :disable="isRunning"
              @click="startHost"
            />
          </div>
          <div class="col-auto">
            <q-btn
              color="negative"
              label="停止房间"
              :disable="!isRunning"
              @click="stopRoom"
            />
          </div>
          <div class="col-auto" v-show="room.state === 'Error'">
            <q-btn color="positive" label="恢复" @click="recoverFromError" />
          </div>
        </div>
      </q-card-section>
    </q-card>

    <!-- 客户端控制 -->
    <q-card flat bordered class="q-mb-md">
      <q-card-section>
        <div class="text-subtitle1 q-mb-sm">🎮 加入房间 (Guest)</div>
        <div class="row q-col-gutter-md">
          <div class="col-12 col-sm-8">
            <q-input v-model="guestRoomCode" label="房间码" dense outlined />
          </div>
          <div class="col-12 col-sm-4">
            <q-btn color="primary" label="粘贴" @click="pasteRoomCode" />
          </div>
        </div>
        <div class="row q-col-gutter-md q-mt-sm">
          <div class="col-12 col-sm-6">
            <q-input v-model="guestPlayerName" label="玩家名称" dense outlined />
          </div>
        </div>
        <div class="row q-mt-sm">
          <div class="col-auto">
            <q-btn color="primary" label="加入房间" :disable="isRunning" @click="joinRoom" />
          </div>
        </div>
      </q-card-section>
    </q-card>

    <!-- 玩家列表 -->
    <q-card flat bordered class="q-mb-md">
      <q-card-section>
        <div class="text-subtitle1 q-mb-sm">👥 玩家列表</div>
        <div v-if="room.players.length === 0" class="text-grey-6 text-center q-pa-md">
          暂无玩家
        </div>
        <div class="row q-col-gutter-md">
          <div v-for="p in room.players" :key="p.machineId" class="col-12 col-md-6">
            <q-card flat bordered>
              <q-card-section>
                <div class="row items-center justify-between q-mb-xs">
                  <div class="text-weight-bold text-primary">{{ p.name || 'Unknown' }}</div>
                  <q-badge
                    :color="p.kind === 'HOST' ? 'positive' : (p.kind === 'GUEST' ? 'primary' : 'grey')"
                  >
                    {{ p.kind }}
                  </q-badge>
                </div>
                <div class="text-caption text-grey-7">
                  <div class="q-mb-xs">
                    <span class="text-grey">Machine ID:</span>
                    <span class="mono">{{ p.machineId || '-' }}</span>
                  </div>
                  <div>
                    <span class="text-grey">Vendor:</span>
                    <span>{{ p.vendor || '-' }}</span>
                  </div>
                </div>
              </q-card-section>
            </q-card>
          </div>
        </div>
      </q-card-section>
    </q-card>

    <!-- 日志 -->
    <q-card flat bordered>
      <q-card-section>
        <div class="text-subtitle1 q-mb-sm">📜 日志</div>
        <div class="log-container">
          <div v-for="(log, idx) in logs" :key="idx" class="log-entry">
            <span class="log-time">{{ log.time }}</span>
            <span :class="logLevelClass(log.level)">[{{ log.level.toUpperCase() }}]</span>
            <span> {{ log.message }}</span>
          </div>
        </div>
      </q-card-section>
    </q-card>
  </q-page>
</template>

<script setup lang="ts">
import { onMounted, onUnmounted, reactive, ref, computed } from 'vue';
import { api } from 'boot/axios';
import type { RoomStatus, PlayerInfo } from 'components/models';

const hostPlayerName = ref('Host');
const guestRoomCode = ref('');
const guestPlayerName = ref('Guest');
const copied = ref(false);

const room = reactive<RoomStatus>({
  state: 'Idle',
  role: null,
  error: null,
  roomCode: null,
  players: [],
  minecraftPort: null,
  lastUpdate: null,
});

const logs = ref<{ level: 'info' | 'warn' | 'error'; message: string; time: string }[]>([
  { level: 'info', message: '控制台已启动', time: '--:--:--' },
]);

let statusTimer: number | null = null;

const isRunning = computedIsRunning(room);

function computedIsRunning(r: RoomStatus) {
  return computed(() => r.state !== 'Idle' && r.state !== 'Stopping');
}

function logLevelClass(level: 'info' | 'warn' | 'error') {
  return {
    'log-level-info': level === 'info',
    'log-level-warn': level === 'warn',
    'log-level-error': level === 'error',
  };
}

function addLog(level: 'info' | 'warn' | 'error', message: string) {
  const time = new Date().toLocaleTimeString();
  logs.value.push({ level, message, time });
  // keep latest 200
  if (logs.value.length > 200) logs.value.shift();
}

function getErrorMessage(e: unknown, fallback: string) {
  if (typeof e === 'string') return e;
  if (typeof e === 'object' && e !== null) {
    const obj = e as Record<string, unknown>;
    const message = obj['message'];
    if (typeof message === 'string') return message;
    const response = obj['response'] as Record<string, unknown> | undefined;
    if (response && typeof response === 'object') {
      const data = response['data'] as Record<string, unknown> | undefined;
      if (data && typeof data === 'object') {
        const err = data['error'];
        if (typeof err === 'string') return err;
      }
    }
  }
  return fallback;
}

async function startHost() {
  const playerName = hostPlayerName.value || 'Host';
  try {
    addLog('info', '正在创建房间...');
    await api.post('/room/host/start', { playerName });
    addLog('info', '房间创建成功！');
    startPolling();
  } catch (e: unknown) {
    const msg = getErrorMessage(e, '创建房间失败');
    addLog('error', msg);
  }
}

async function joinRoom() {
  const roomCode = guestRoomCode.value.trim();
  const playerName = guestPlayerName.value || 'Guest';
  if (!roomCode) {
    addLog('warn', '请输入房间码');
    return;
  }
  try {
    addLog('info', `正在加入房间 ${roomCode}...`);
    await api.post('/room/guest/start', { roomCode, playerName });
    addLog('info', '正在连接房间...');
    startPolling();
  } catch (e: unknown) {
    const msg = getErrorMessage(e, '加入房间失败');
    addLog('error', msg);
  }
}

async function stopRoom() {
  try {
    addLog('info', '正在停止房间...');
    await api.post('/room/stop');
    addLog('info', '房间已停止');
    Object.assign(room, {
      state: 'Idle',
      role: null,
      players: [],
      minecraftPort: null,
      roomCode: null,
      lastUpdate: new Date().toISOString(),
      error: null,
    });
  } catch (e: unknown) {
    const msg = getErrorMessage(e, '停止房间失败');
    addLog('error', msg);
  }
}

async function recoverFromError() {
  try {
    addLog('info', '正在从错误状态恢复...');
    await api.post('/room/retry');
    addLog('info', '正在重新连接...');
    startPolling();
  } catch (e: unknown) {
    const msg = getErrorMessage(e, '恢复失败');
    addLog('error', msg);
  }
}

async function updateStatus() {
  try {
    const { data } = await api.get<RoomStatus>('/room/status');
    updateUI(data);
  } catch (e) {
    // silent to console
    console.error('Failed to fetch status:', e);
  }
}

function updateUI(data: RoomStatus) {
  room.state = data.state;
  room.role = data.role ?? null;
  room.minecraftPort = data.minecraftPort ?? null;
  room.players = normalizePlayers(data.players || []);
  room.roomCode = data.roomCode ?? null;
  room.lastUpdate = data.lastUpdate ?? null;
}

function normalizePlayers(players: PlayerInfo[]) {
  return players.map((p) => ({
    name: p.name,
    machineId: p.machineId,
    vendor: p.vendor,
    kind: p.kind,
  }));
}

function startPolling() {
  if (statusTimer) return;
  void updateStatus();
  statusTimer = window.setInterval(() => void updateStatus(), 2000);
}

function stopPolling() {
  if (statusTimer) {
    clearInterval(statusTimer);
    statusTimer = null;
  }
}

async function copyRoomCode() {
  const code = room.roomCode;
  if (!code) {
    addLog('warn', '没有可复制的房间码');
    return;
  }
  try {
    await navigator.clipboard.writeText(code);
    addLog('info', '房间码已复制到粘贴板');
    copied.value = true;
    setTimeout(() => (copied.value = false), 2000);
  } catch (e: unknown) {
    const msg = getErrorMessage(e, String(e));
    addLog('error', `复制失败: ${msg}`);
  }
}

async function pasteRoomCode() {
  try {
    const text = await navigator.clipboard.readText();
    guestRoomCode.value = (text || '').trim();
    addLog('info', '已从粘贴板粘贴房间码');
  } catch (e: unknown) {
    const msg = getErrorMessage(e, String(e));
    addLog('warn', `无法读取粘贴板: ${msg}`);
  }
}

onMounted(() => {
  startPolling();
});

onUnmounted(() => {
  stopPolling();
});
</script>

<style scoped>
.log-container {
  background: #0a0a0a;
  border-radius: 8px;
  padding: 12px;
  height: 300px;
  overflow-y: auto;
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 12px;
}
.log-entry {
  padding: 4px 0;
  border-bottom: 1px solid #1a1a2e;
}
.log-time {
  color: #666;
  margin-right: 10px;
}
.log-level-info {
  color: #00d4ff;
}
.log-level-warn {
  color: #ffa502;
}
.log-level-error {
  color: #ff4757;
}
.room-code {
  font-family: 'Consolas', monospace;
  font-size: 24px;
  color: #00ff88;
  text-align: center;
  padding: 12px;
  background: #0a0a0a;
  border-radius: 8px;
  letter-spacing: 2px;
  user-select: all;
  cursor: pointer;
}
.mono {
  font-family: 'Consolas', monospace;
  font-size: 11px;
}
</style>
