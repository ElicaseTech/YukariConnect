export interface Todo {
  id: number;
  content: string;
}

export interface Meta {
  totalCount: number;
}

export interface PlayerInfo {
  name: string;
  machineId: string;
  vendor: string;
  kind: string;
}

export interface RoomStatus {
  state: string;
  role: string | null;
  error: string | null;
  roomCode: string | null;
  players: PlayerInfo[];
  minecraftPort: number | null;
  lastUpdate: string | null;
}
