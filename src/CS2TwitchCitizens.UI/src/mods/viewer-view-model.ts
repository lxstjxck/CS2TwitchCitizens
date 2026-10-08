export type ViewerSummary = {
  twitchUserId: string;
  login: string;
  displayName: string;
  citizenName: string;
  isValid: boolean;
  positionAvailable: boolean;
  currentLifeStatus: string;
  totalLives: number;
};

export function selectedViewer<T extends ViewerSummary>(viewers: T[], selectedId: string): T | undefined {
  return viewers.find((viewer) => viewer.twitchUserId === selectedId) ?? viewers[0];
}

export function canLocate(viewer?: ViewerSummary): boolean {
  return !!viewer && viewer.isValid && viewer.positionAvailable && viewer.currentLifeStatus === "Active";
}

export function isCameraError(result?: string): boolean {
  return result === "CitizenNotFound" || result === "PositionUnavailable" ||
    result === "CameraUnavailable" || result === "FocusFailed";
}

export type LifeSummary = {
  lifeId: string; originalCitizenName: string; twitchDisplayName: string;
  startGameDate: string; endGameDate: string; status: string;
  lastKnownAge: string; lastKnownHome: string; lastKnownWorkplace: string;
  causeOfDeath: string;
};

export function lifeHistory<T extends LifeSummary>(current: T | null | undefined, previous: T[]): Array<{ number: number; life: T }> {
  const lives = [...previous, ...(current ? [current] : [])];
  return lives.map((life, index) => ({ number: index + 1, life })).reverse();
}
