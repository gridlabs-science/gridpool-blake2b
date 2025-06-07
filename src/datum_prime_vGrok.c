// datum_prime_server.c
// DATUM Prime server implementation for pooled Bitcoin mining
// Interacts with DATUM Gateway using the DATUM Protocol
// Implements only the specified client/server messages for basic communication

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <stdbool.h>
#include <arpa/inet.h>
#include <sys/socket.h>
#include <unistd.h>
#include <openssl/ec.h>
#include <openssl/ecdsa.h>
#include <openssl/obj_mac.h>
#include <openssl/bn.h>
#include <openssl/rand.h>
#include <openssl/evp.h>
#include <openssl/pem.h>

// Include DATUM Gateway protocol headers (assumed to be in the same directory or include path)
#include "datum_protocol.h"

// Define constants from the DATUM Protocol
#define DATUM_PROTOCOL_HELLO 0x01
#define DATUM_PROTOCOL_HANDSHAKE_RESPONSE 0x02
#define DATUM_PROTOCOL_MINING 0x05
#define DATUM_PROTOCOL_COINBASER_FETCH 0x10
#define DATUM_PROTOCOL_COINBASER_FETCH_RESPONSE 0x11
#define DATUM_PROTOCOL_POW_SUBMIT 0x27
#define DATUM_PROTOCOL_SHARE_RESPONSE 0x8F
#define DATUM_POW_SHARE_RESPONSE_ACCEPTED 0x50

// Default coinbase address for payouts
#define DEFAULT_COINBASE_ADDRESS "mpuPt3FvAfwQFxd6BmPrwuRBbdMgmDSGfH"
// Coinbase tag and unique ID
#define COINBASE_TAG "Boot protocol"
#define UNIQUE_ID "21"
// Minimum difficulty (example value, adjust as needed)
#define MINIMUM_DIFFICULTY 1000
// Message of the day
#define MOTD "Welcome to DATUM Prime Server!"

// Structure for server session state
typedef struct {
    EC_KEY *server_key;           // Server's ECDSA key pair
    EC_KEY *client_pubkey;       // Client's public key from hello
    EC_KEY *session_key;         // Session key for encryption
    uint8_t *client_session_key; // Client-provided session key (raw bytes)
    size_t client_session_key_len;
} ServerSession;

// Function to generate a static ECDSA key pair (secp256k1) and print the public key
EC_KEY* generate_key_pair(void) {
    // Create a new EC key using the secp256k1 curve (Bitcoin standard)
    EC_KEY *key = EC_KEY_new_by_curve_name(NID_secp256k1);
    if (!key) {
        fprintf(stderr, "Failed to create EC key\n");
        return NULL;
    }

    // Generate the key pair
    if (!EC_KEY_generate_key(key)) {
        fprintf(stderr, "Failed to generate EC key pair\n");
        EC_KEY_free(key);
        return NULL;
    }

    // Convert public key to raw bytes for inclusion in config.json
    const EC_POINT *pub_key = EC_KEY_get0_public_key(key);
    BN_CTX *ctx = BN_CTX_new();
    char *pub_key_hex = EC_POINT_point2hex(EC_KEY_get0_group(key), pub_key, POINT_CONVERSION_UNCOMPRESSED, ctx);
    printf("Server Public Key (for config.json):\n%s\n", pub_key_hex);
    OPENSSL_free(pub_key_hex);
    BN_CTX_free(ctx);

    return key;
}

// Function to decrypt a message using the server's private key
// Simplified: assumes the message is encrypted with server's public key
uint8_t* decrypt_message(EC_KEY *server_key, uint8_t *encrypted, size_t encrypted_len, size_t *decrypted_len) {
    // Placeholder: Implement actual decryption logic (e.g., ECIES or similar)
    // For simplicity, assume the message is the plaintext for now
    *decrypted_len = encrypted_len;
    uint8_t *decrypted = malloc(encrypted_len);
    memcpy(decrypted, encrypted, encrypted_len);
    return decrypted;
}

// Function to encrypt a message with the client's session key
// Simplified: returns the plaintext for now
uint8_t* encrypt_message(uint8_t *client_session_key, size_t key_len, uint8_t *data, size_t data_len, size_t *encrypted_len) {
    *encrypted_len = data_len;
    uint8_t *encrypted = malloc(data_len);
    memcpy(encrypted, data, data_len);
    return encrypted;
}

// Function to sign a message with the server's private key
uint8_t* sign_message(EC_KEY *server_key, uint8_t *data, size_t data_len, size_t *sig_len) {
    // Create a signature using ECDSA
    ECDSA_SIG *sig = ECDSA_do_sign(data, data_len, server_key);
    if (!sig) {
        fprintf(stderr, "Failed to sign message\n");
        return NULL;
    }

    // Serialize the signature
    *sig_len = i2d_ECDSA_SIG(sig, NULL);
    uint8_t *sig_buf = malloc(*sig_len);
    uint8_t *p = sig_buf;
    i2d_ECDSA_SIG(sig, &p);
    ECDSA_SIG_free(sig);
    return sig_buf;
}

// Function to send a message with the DATUM_PROTOCOL_HEADER
int send_message(int client_sock, uint8_t proto_cmd, uint8_t *payload, size_t payload_len, bool is_signed, bool is_encrypted_channel, EC_KEY *server_key, uint8_t *client_session_key, size_t client_session_key_len) {
    T_DATUM_PROTOCOL_HEADER header = {
        .cmd_len = payload_len,
        .reserved = 0,
        .is_signed = is_signed,
        .is_encrypted_pubkey = 0,
        .is_encrypted_channel = is_encrypted_channel,
        .proto_cmd = proto_cmd
    };

    // Encrypt payload if needed
    size_t encrypted_len;
    uint8_t *encrypted_payload = is_encrypted_channel ?
        encrypt_message(client_session_key, client_session_key_len, payload, payload_len, &encrypted_len) :
        payload;

    // Sign the payload if needed
    size_t sig_len = 0;
    uint8_t *signature = NULL;
    if (is_signed) {
        signature = sign_message(server_key, encrypted_payload, encrypted_len, &sig_len);
        if (!signature) {
            if (is_encrypted_channel) free(encrypted_payload);
            return -1;
        }
    }

    // Send header
    uint8_t header_buf[sizeof(T_DATUM_PROTOCOL_HEADER)];
    memcpy(header_buf, &header, sizeof(header));
    if (send(client_sock, header_buf, sizeof(header_buf), 0) < 0) {
        fprintf(stderr, "Failed to send header\n");
        if (is_encrypted_channel) free(encrypted_payload);
        if (signature) free(signature);
        return -1;
    }

    // Send payload
    if (send(client_sock, encrypted_payload, encrypted_len, 0) < 0) {
        fprintf(stderr, "Failed to send payload\n");
        if (is_encrypted_channel) free(encrypted_payload);
        if (signature) free(signature);
        return -1;
    }

    // Send signature if present
    if (is_signed && signature) {
        if (send(client_sock, signature, sig_len, 0) < 0) {
            fprintf(stderr, "Failed to send signature\n");
            if (is_encrypted_channel) free(encrypted_payload);
            free(signature);
            return -1;
        }
        free(signature);
    }

    if (is_encrypted_channel) free(encrypted_payload);
    return 0;
}

// Function to handle the hello message (0x01)
void handle_hello(int client_sock, ServerSession *session, uint8_t *data, size_t data_len) {
    // Decrypt the message with the server's private key
    size_t decrypted_len;
    uint8_t *decrypted = decrypt_message(session->server_key, data, data_len, &decrypted_len);
    if (!decrypted) {
        fprintf(stderr, "Failed to decrypt hello message\n");
        return;
    }

    // Parse client public key and session key (simplified: assume they're in the message)
    // For now, store a dummy client session key
    session->client_session_key_len = decrypted_len;
    session->client_session_key = malloc(decrypted_len);
    memcpy(session->client_session_key, decrypted, decrypted_len);
    free(decrypted);

    // Prepare handshake response (0x02)
    // Payload: client pubkey, session key, pool session key, coinbase tag, unique ID, min difficulty, MOTD
    char payload[1024];
    size_t offset = 0;
    memcpy(payload + offset, session->client_session_key, session->client_session_key_len);
    offset += session->client_session_key_len;

    // Add pool session key (simplified: use server key as placeholder)
    const EC_POINT *pool_session_key = EC_KEY_get0_public_key(session->server_key);
    size_t pool_key_len = EC_POINT_point2oct(EC_KEY_get0_group(session->server_key),
                                            pool_session_key, POINT_CONVERSION_UNCOMPRESSED,
                                            (uint8_t*)payload + offset, sizeof(payload) - offset, NULL);
    offset += pool_key_len;

    // Add coinbase tag, unique ID, minimum difficulty, MOTD
    strcpy(payload + offset, COINBASE_TAG);
    offset += strlen(COINBASE_TAG) + 1;
    strcpy(payload + offset, UNIQUE_ID);
    offset += strlen(UNIQUE_ID) + 1;
    *(uint32_t*)(payload + offset) = htonl(MINIMUM_DIFFICULTY);
    offset += sizeof(uint32_t);
    strcpy(payload + offset, MOTD);
    offset += strlen(MOTD) + 1;

    // Send handshake response
    if (send_message(client_sock, DATUM_PROTOCOL_HANDSHAKE_RESPONSE, (uint8_t*)payload, offset, true, true, session->server_key, session->client_session_key, session->client_session_key_len) < 0) {
        fprintf(stderr, "Failed to send handshake response\n");
    }
}

// Function to handle coinbaser_fetch (0x05, subcommand 0x10)
void handle_coinbaser_fetch(int client_sock, ServerSession *session, uint8_t *data, size_t data_len) {
    // Parse block reward value (simplified: assume it's at the start of the data)
    uint64_t block_reward;
    if (data_len >= sizeof(uint64_t)) {
        block_reward = *(uint64_t*)data;
        printf("Received block reward: %lu\n", block_reward);
    }

    // Prepare coinbaser_fetch_response (0x05, subcommand 0x11)
    // Payload: list of payout addresses (just the default address for now)
    char payload[256];
    size_t offset = 0;
    *(uint8_t*)(payload + offset) = DATUM_PROTOCOL_COINBASER_FETCH_RESPONSE;
    offset += 1;
    strcpy(payload + offset, DEFAULT_COINBASE_ADDRESS);
    offset += strlen(DEFAULT_COINBASE_ADDRESS) + 1;

    // Send response
    if (send_message(client_sock, DATUM_PROTOCOL_MINING, (uint8_t*)payload, offset, false, true, session->server_key, session->client_session_key, session->client_session_key_len) < 0) {
        fprintf(stderr, "Failed to send coinbaser fetch response\n");
    }
}

// Function to handle proof-of-work submission (0x05, subcommand 0x27)
void handle_pow_submit(int client_sock, ServerSession *session, uint8_t *data, size_t data_len) {
    // Simplified: assume difficulty is included in the message
    uint32_t difficulty = *(uint32_t*)data;
    printf("Received PoW submission with difficulty: %u\n", difficulty);

    // Prepare share response (0x05, subcommand 0x8F, accepted)
    uint8_t payload[2];
    payload[0] = DATUM_PROTOCOL_SHARE_RESPONSE;
    payload[1] = DATUM_POW_SHARE_RESPONSE_ACCEPTED;

    // Send response
    if (send_message(client_sock, DATUM_PROTOCOL_MINING, payload, 2, false, true, session->server_key, session->client_session_key, session->client_session_key_len) < 0) {
        fprintf(stderr, "Failed to send share response\n");
    }
}

// Main server function
int main(void) {
    // Initialize OpenSSL
    OpenSSL_add_all_algorithms();

    // Generate server key pair
    ServerSession session = {0};
    session.server_key = generate_key_pair();
    if (!session.server_key) {
        fprintf(stderr, "Failed to initialize server key\n");
        return 1;
    }

    // Set up TCP server
    int server_sock = socket(AF_INET, SOCK_STREAM, 0);
    if (server_sock < 0) {
        fprintf(stderr, "Failed to create socket\n");
        EC_KEY_free(session.server_key);
        return 1;
    }

    struct sockaddr_in server_addr = {
        .sin_family = AF_INET,
        .sin_addr.s_addr = INADDR_ANY,
        .sin_port = htons(8334)
    };

    if (bind(server_sock, (struct sockaddr*)&server_addr, sizeof(server_addr)) < 0) {
        fprintf(stderr, "Failed to bind socket\n");
        close(server_sock);
        EC_KEY_free(session.server_key);
        return 1;
    }

    if (listen(server_sock, 5) < 0) {
        fprintf(stderr, "Failed to listen on socket\n");
        close(server_sock);
        EC_KEY_free(session.server_key);
        return 1;
    }

    printf("DATUM Prime Server listening on port 8334...\n");

    // Main loop
    while (1) {
        struct sockaddr_in client_addr;
        socklen_t client_len = sizeof(client_addr);
        int client_sock = accept(server_sock, (struct sockaddr*)&client_addr, &client_len);
        if (client_sock < 0) {
            fprintf(stderr, "Failed to accept connection\n");
            continue;
        }

        // Receive and process messages
        while (1) {
            T_DATUM_PROTOCOL_HEADER header;
            if (recv(client_sock, &header, sizeof(header), 0) <= 0) {
                fprintf(stderr, "Client disconnected or error\n");
                break;
            }

            // Allocate buffer for payload
            uint8_t *payload = malloc(header.cmd_len);
            if (!payload) {
                fprintf(stderr, "Failed to allocate payload buffer\n");
                break;
            }

            // Receive payload
            if (recv(client_sock, payload, header.cmd_len, 0) != header.cmd_len) {
                fprintf(stderr, "Failed to receive payload\n");
                free(payload);
                break;
            }

            // Handle messages
            if (header.proto_cmd == DATUM_PROTOCOL_HELLO) {
                handle_hello(client_sock, &session, payload, header.cmd_len);
            } else if (header.proto_cmd == DATUM_PROTOCOL_MINING) {
                uint8_t subcommand = payload[0];
                if (subcommand == DATUM_PROTOCOL_COINBASER_FETCH) {
                    handle_coinbaser_fetch(client_sock, &session, payload + 1, header.cmd_len - 1);
                } else if (subcommand == DATUM_PROTOCOL_POW_SUBMIT) {
                    handle_pow_submit(client_sock, &session, payload + 1, header.cmd_len - 1);
                }
            }

            free(payload);
        }

        // Clean up client session
        if (session.client_session_key) {
            free(session.client_session_key);
            session.client_session_key = NULL;
            session.client_session_key_len = 0;
        }
        close(client_sock);
    }

    // Cleanup
    close(server_sock);
    EC_KEY_free(session.server_key);
    EVP_cleanup();
    return 0;
}