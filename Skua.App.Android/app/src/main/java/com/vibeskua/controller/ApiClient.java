package com.vibeskua.controller;

import android.os.Handler;
import android.os.Looper;

import java.io.BufferedReader;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.Inet4Address;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.NetworkInterface;
import java.net.Socket;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.Collections;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;

public class ApiClient {

    private static final ExecutorService executor = Executors.newCachedThreadPool();
    private static final Handler mainHandler = new Handler(Looper.getMainLooper());

    public interface Callback<T> {
        void onSuccess(T result);
        void onError(Exception error);
    }

    public static String get(String urlString, int timeoutMs) throws Exception {
        return get(urlString, null, timeoutMs);
    }

    public static String get(String urlString, Map<String, String> headers, int timeoutMs) throws Exception {
        HttpURLConnection conn = null;
        try {
            URL url = new URL(urlString);
            conn = (HttpURLConnection) url.openConnection();
            conn.setRequestMethod("GET");
            conn.setConnectTimeout(timeoutMs);
            conn.setReadTimeout(timeoutMs);
            if (headers != null) {
                for (Map.Entry<String, String> e : headers.entrySet()) {
                    conn.setRequestProperty(e.getKey(), e.getValue());
                }
            }

            int code = conn.getResponseCode();
            if (code >= 200 && code < 300) {
                return readStream(conn.getInputStream());
            } else {
                InputStream err = conn.getErrorStream();
                String errStr = err != null ? readStream(err) : "HTTP Error " + code;
                throw new Exception(errStr);
            }
        } finally {
            if (conn != null) {
                conn.disconnect();
            }
        }
    }

    public static String post(String urlString, String jsonBody, int timeoutMs) throws Exception {
        return post(urlString, jsonBody, null, timeoutMs);
    }

    public static String post(String urlString, String jsonBody, Map<String, String> headers, int timeoutMs) throws Exception {
        HttpURLConnection conn = null;
        try {
            URL url = new URL(urlString);
            conn = (HttpURLConnection) url.openConnection();
            conn.setRequestMethod("POST");
            conn.setRequestProperty("Content-Type", "application/json; charset=utf-8");
            conn.setConnectTimeout(timeoutMs);
            conn.setReadTimeout(timeoutMs);
            conn.setDoOutput(true);
            if (headers != null) {
                for (Map.Entry<String, String> e : headers.entrySet()) {
                    conn.setRequestProperty(e.getKey(), e.getValue());
                }
            }

            if (jsonBody != null && !jsonBody.isEmpty()) {
                byte[] bytes = jsonBody.getBytes(StandardCharsets.UTF_8);
                try (OutputStream os = conn.getOutputStream()) {
                    os.write(bytes);
                    os.flush();
                }
            }

            int code = conn.getResponseCode();
            if (code >= 200 && code < 300) {
                return readStream(conn.getInputStream());
            } else {
                InputStream err = conn.getErrorStream();
                String errStr = err != null ? readStream(err) : "HTTP Error " + code;
                throw new Exception(errStr);
            }
        } finally {
            if (conn != null) {
                conn.disconnect();
            }
        }
    }

    public static void getAsync(String url, Callback<String> callback) {
        getAsync(url, null, 15000, callback);
    }

    public static void getAsync(String url, int timeoutMs, Callback<String> callback) {
        getAsync(url, null, timeoutMs, callback);
    }

    public static void getAsync(String url, Map<String, String> headers, int timeoutMs, Callback<String> callback) {
        executor.execute(() -> {
            try {
                String res = get(url, headers, timeoutMs);
                mainHandler.post(() -> callback.onSuccess(res));
            } catch (Exception ex) {
                AppLogger.e("ApiClient", "GET failed [" + url + "]: " + ex.getMessage());
                mainHandler.post(() -> callback.onError(ex));
            }
        });
    }

    public static void getAsync(String url, Map<String, String> headers, Callback<String> callback) {
        getAsync(url, headers, 15000, callback);
    }

    public static void postAsync(String url, String jsonBody, Callback<String> callback) {
        postAsync(url, jsonBody, null, 15000, callback);
    }

    public static void postAsync(String url, String jsonBody, int timeoutMs, Callback<String> callback) {
        postAsync(url, jsonBody, null, timeoutMs, callback);
    }

    public static void postAsync(String url, String jsonBody, Map<String, String> headers, Callback<String> callback) {
        postAsync(url, jsonBody, headers, 15000, callback);
    }

    public static void postAsync(String url, String jsonBody, Map<String, String> headers, int timeoutMs, Callback<String> callback) {
        executor.execute(() -> {
            try {
                String res = post(url, jsonBody, headers, timeoutMs);
                AppLogger.i("ApiClient", "POST success [" + url + "]");
                mainHandler.post(() -> callback.onSuccess(res));
            } catch (Exception ex) {
                AppLogger.e("ApiClient", "POST failed [" + url + "]: " + ex.getMessage());
                mainHandler.post(() -> callback.onError(ex));
            }
        });
    }

    /**
     * Determines local IPv4 subnet prefix (e.g. 192.168.1 or 10.0.0) without needing runtime location permission.
     */
    public static String getLocalSubnet() {
        try {
            List<NetworkInterface> interfaces = Collections.list(NetworkInterface.getNetworkInterfaces());
            for (NetworkInterface intf : interfaces) {
                if (intf.isLoopback() || !intf.isUp()) continue;
                List<InetAddress> addrs = Collections.list(intf.getInetAddresses());
                for (InetAddress addr : addrs) {
                    if (!addr.isLoopbackAddress() && addr instanceof Inet4Address) {
                        String host = addr.getHostAddress();
                        if (host != null && !host.startsWith("127.")) {
                            int lastDot = host.lastIndexOf('.');
                            if (lastDot > 0) {
                                return host.substring(0, lastDot);
                            }
                        }
                    }
                }
            }
        } catch (Exception ignored) { }
        return "";
    }

    /**
     * Scans local /24 subnet using a safe bounded worker pool so older/low-end Android devices
     * never run out of memory or native threads.
     */
    public static void scanSubnetAsync(String baseSubnet, int port, Callback<String> onFoundCallback) {
        if (baseSubnet == null || baseSubnet.trim().isEmpty()) {
            mainHandler.post(() -> onFoundCallback.onError(new Exception("No active WiFi or local network detected to scan.")));
            return;
        }
        executor.execute(() -> {
            ExecutorService scannerPool = Executors.newFixedThreadPool(24);
            AtomicBoolean found = new AtomicBoolean(false);
            AtomicInteger remaining = new AtomicInteger(254);

            for (int i = 1; i < 255; i++) {
                final String host = baseSubnet + "." + i;
                scannerPool.execute(() -> {
                    if (found.get()) {
                        remaining.decrementAndGet();
                        return;
                    }

                    try (Socket socket = new Socket()) {
                        socket.connect(new InetSocketAddress(host, port), 300);
                        String res = get("http://" + host + ":" + port + "/api/ping", 600);
                        if (res.contains("VibeSkua") && found.compareAndSet(false, true)) {
                            mainHandler.post(() -> onFoundCallback.onSuccess(host));
                        }
                    } catch (Exception ignored) {
                    } finally {
                        if (remaining.decrementAndGet() == 0 && !found.get()) {
                            mainHandler.post(() -> onFoundCallback.onError(new Exception("No VibeSkua host found on " + baseSubnet + ".x")));
                        }
                    }
                });
            }
            scannerPool.shutdown();
        });
    }

    private static String readStream(InputStream is) throws Exception {
        try (InputStreamReader reader = new InputStreamReader(is, StandardCharsets.UTF_8)) {
            StringBuilder sb = new StringBuilder();
            char[] buf = new char[8192];
            int read;
            while ((read = reader.read(buf)) != -1) {
                sb.append(buf, 0, read);
            }
            return sb.toString();
        }
    }
}
