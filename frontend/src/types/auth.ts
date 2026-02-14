export type AuthStatus = {
  isSetupCompleted: boolean;
  isAuthenticated: boolean;
};

export type LoginRequest = {
  username: string;
  password: string;
};

export type LoginResponse = {
  token: string;
  username: string;
  expiresAt: string;
};

export type SetupQbittorrentRequest = {
  host: string;
  port: number;
  username: string;
  password: string;
  defaultSavePath: string;
  category?: string;
  tags?: string;
};

export type SetupDownloadPreferences = {
  subgroup: string;
  resolution: string;
  subtitleType: string;
  priorityOrder: string[];
};

export type SetupRequest = {
  username: string;
  password: string;
  tmdbToken: string;
  qbittorrent: SetupQbittorrentRequest;
  downloadPreferences?: SetupDownloadPreferences;
};
