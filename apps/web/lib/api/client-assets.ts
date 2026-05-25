import { apiRequest } from "@/lib/api/client";

export interface UploadClientImageAssetResponse {
  url: string;
  publicPath: string;
  storedFileName: string;
  contentType: string;
  sizeBytes: number;
  originalFileName: string;
}

export async function uploadClientImageAsset(
  clientId: string,
  file: File,
  token?: string,
): Promise<UploadClientImageAssetResponse> {
  const form = new FormData();
  form.append("file", file);

  return apiRequest<UploadClientImageAssetResponse>(
    `/api/social/clients/${clientId}/assets/images`,
    {
      method: "POST",
      body: form,
    },
    token,
  );
}
